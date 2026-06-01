using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Settings;

namespace PaymentServices.RTPSend.Services;

public interface IPaymentOrchestrator
{
    /// <summary>
    /// Runs RTPSend's portion of the pipeline: ACCOUNTLOOKUP (partner-ledger)
    /// then calls Gateway /tptch/send. The async pipeline (AccountResolution →
    /// Transfer) and the rtpsend-outcome handler take it from there.
    /// </summary>
    Task<EvolvePaymentRequest> ProcessAsync(EvolvePaymentRequest payment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes from whatever stage the payment is currently in (stage-aware SB
    /// redelivery). RTPSend now only owns ACCOUNTLOOKUP → Gateway hand-off.
    /// </summary>
    Task<EvolvePaymentRequest> ResumeFromAsync(EvolvePaymentRequest payment, CancellationToken cancellationToken = default);
}

public sealed class PaymentOrchestrator : IPaymentOrchestrator
{
    private readonly IPartnerLedgerSystem _partnerLedger;
    private readonly IGatewayClient _gatewayClient;
    private readonly IPaymentCosmosDBAdapter _paymentCosmosDB;
    private readonly RtpSendSettings _settings;
    private readonly ILogger<PaymentOrchestrator> _logger;

    public PaymentOrchestrator(
        IPartnerLedgerSystem partnerLedger,
        IGatewayClient gatewayClient,
        IPaymentCosmosDBAdapter paymentCosmosDB,
        IOptions<RtpSendSettings> settings,
        ILogger<PaymentOrchestrator> logger)
    {
        _partnerLedger = partnerLedger;
        _gatewayClient = gatewayClient;
        _paymentCosmosDB = paymentCosmosDB;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<EvolvePaymentRequest> ProcessAsync(
        EvolvePaymentRequest payment, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Full pipeline for evolveId {EvolveId}", payment.EvolveId);
        return await RunStagesAsync(payment, startStage: RequestStage.ACCOUNTLOOKUP, cancellationToken);
    }

    public async Task<EvolvePaymentRequest> ResumeFromAsync(
        EvolvePaymentRequest payment, CancellationToken cancellationToken = default)
    {
        var startStage = DetermineResumeStage(payment);
        if (startStage is null)
        {
            _logger.LogInformation(
                "Payment {EvolveId} is already in terminal state ({Status}); nothing to resume.",
                payment.EvolveId, payment.Status);
            return payment;
        }

        _logger.LogInformation(
            "Resuming evolveId {EvolveId} from stage {Stage}", payment.EvolveId, startStage);
        return await RunStagesAsync(payment, startStage.Value, cancellationToken);
    }

    /// <summary>
    /// Reads payment.Stage + payment.Status from the persisted Cosmos document
    /// and decides which stage to start from. If already completed or terminally
    /// failed, returns null.
    /// </summary>
    private static RequestStage? DetermineResumeStage(EvolvePaymentRequest payment)
    {
        // Already fully processed — no resume needed.
        if (payment.Status == RequestStatus.COMPLETED.ToString())
            return null;

        // Terminal NSF failure — no resume, ever. The funds situation isn't
        // going to fix itself via retry.
        if (payment.Status == RequestStatus.FAILED_NSF.ToString())
            return null;

        // Map last attempted stage → which stage to retry from. RTPSend now
        // only owns ACCOUNTLOOKUP + the Gateway hand-off, so every non-terminal
        // resume restarts at ACCOUNTLOOKUP (idempotent: partner-ledger lookup
        // and Gateway dedupe both tolerate replays).
        return payment.Stage switch
        {
            nameof(RequestStage.RTP_API) => RequestStage.ACCOUNTLOOKUP,
            nameof(RequestStage.ACCOUNTLOOKUP) => RequestStage.ACCOUNTLOOKUP,
            _ => RequestStage.ACCOUNTLOOKUP
        };
    }

    private async Task<EvolvePaymentRequest> RunStagesAsync(
        EvolvePaymentRequest payment, RequestStage startStage, CancellationToken cancellationToken)
    {
        // ----- Stage: PartnerLedger (ACCOUNTLOOKUP) -----------------------
        // Resolves source account → FBO account and enriches the doc.
        if (startStage <= RequestStage.ACCOUNTLOOKUP)
            payment = await _partnerLedger.PerformAccountLookupUpdate(payment);

        // ----- Stage: call Gateway /tptch/send ----------------------------
        // Gateway validates/dedupes/persists and publishes to the async
        // pipeline (AccountResolution → Transfer). The eventual outcome
        // (TransferCompleted/Failed/AccountResolutionFailed) comes back to
        // RTPSend via the rtpsend-outcome subscription, where TabaPay runs.
        await _gatewayClient.SendAsync(payment, cancellationToken);

        // Patch status: handed off to Gateway. Stage stays at ACCOUNTLOOKUP
        // (its last RTPSend-owned stage); status INITIATED signals "in flight,
        // awaiting pipeline outcome". No outcome envelope is published here.
        var patches = EvolvePaymentRequestHelper.GetStatusPatchOperation(
            RequestStage.ACCOUNTLOOKUP,
            RequestStatus.INITIATED,
            additionalInfo: new { Message = "Submitted to Gateway tptch/send" });

        await _paymentCosmosDB.PatchItemAsync(payment, patches);

        payment.Stage = RequestStage.ACCOUNTLOOKUP.ToString();
        payment.Status = RequestStatus.INITIATED.ToString();

        _logger.LogInformation(
            "Payment {EvolveId} submitted to Gateway; awaiting pipeline outcome.",
            payment.EvolveId);

        return payment;
    }
}