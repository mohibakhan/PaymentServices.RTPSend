using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface.External;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Settings;

namespace PaymentServices.RTPSend.Services;

public interface IPaymentOrchestrator
{
    /// <summary>
    /// Runs the full pipeline: PartnerLedger → Limit → Ledger → TabaPay.
    /// Publishes a terminal outcome envelope on completion.
    /// </summary>
    Task<EvolvePaymentRequest> ProcessAsync(EvolvePaymentRequest payment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes from whatever stage the payment is currently in. Used by the
    /// DLQ-drain timer. Reads <see cref="EvolvePaymentRequest.Stage"/> and
    /// runs only the remaining stages.
    /// </summary>
    Task<EvolvePaymentRequest> ResumeFromAsync(EvolvePaymentRequest payment, CancellationToken cancellationToken = default);
}

public sealed class PaymentOrchestrator : IPaymentOrchestrator
{
    private readonly PartnerLedgerSystem _partnerLedger;
    private readonly ILimitService _limitService;
    private readonly ILedgerService _ledgerService;
    private readonly ITabaPaySendService _tabaPay;
    private readonly IServiceBusMessageService _serviceBus;
    private readonly RtpSendSettings _settings;
    private readonly ILogger<PaymentOrchestrator> _logger;

    public PaymentOrchestrator(
        PartnerLedgerSystem partnerLedger,
        ILimitService limitService,
        ILedgerService ledgerService,
        ITabaPaySendService tabaPay,
        IServiceBusMessageService serviceBus,
        IOptions<RtpSendSettings> settings,
        ILogger<PaymentOrchestrator> logger)
    {
        _partnerLedger = partnerLedger;
        _limitService = limitService;
        _ledgerService = ledgerService;
        _tabaPay = tabaPay;
        _serviceBus = serviceBus;
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
    /// and decides which stage to start from. If already completed, returns null.
    /// </summary>
    private static RequestStage? DetermineResumeStage(EvolvePaymentRequest payment)
    {
        // Already fully processed — no resume needed.
        if (payment.Status == RequestStatus.COMPLETED.ToString())
            return null;

        // Map last attempted stage → which stage to retry from.
        return payment.Stage switch
        {
            // Initial state: never started
            nameof(RequestStage.RTP_API)        => RequestStage.ACCOUNTLOOKUP,

            // Partner-ledger failed: retry from there
            nameof(RequestStage.ACCOUNTLOOKUP)  => RequestStage.ACCOUNTLOOKUP,

            // Limit check failed: retry from limit
            nameof(RequestStage.LIMIT)          => RequestStage.LIMIT,

            // Ledger reservation failed: retry from ledger
            nameof(RequestStage.LEDGER)         => RequestStage.LEDGER,

            // TabaPay failed: retry TabaPay only
            nameof(RequestStage.TABAPAY)        => RequestStage.TABAPAY,

            // Anything unexpected: start over from the beginning
            _                                    => RequestStage.ACCOUNTLOOKUP
        };
    }

    private async Task<EvolvePaymentRequest> RunStagesAsync(
        EvolvePaymentRequest payment, RequestStage startStage, CancellationToken cancellationToken)
    {
        // ----- Stage: PartnerLedger ---------------------------------------
        if (startStage <= RequestStage.ACCOUNTLOOKUP)
            payment = await _partnerLedger.PerformAccountLookupUpdate(payment);

        // ----- Stage: Limit -----------------------------------------------
        if (startStage <= RequestStage.LIMIT)
            await CheckLimitAsync(payment, cancellationToken);

        // ----- Stage: Ledger ----------------------------------------------
        if (startStage <= RequestStage.LEDGER)
            await ReserveLedgerAsync(payment, cancellationToken);

        // ----- Stage: TabaPay ---------------------------------------------
        try
        {
            var sendResult = await _tabaPay.ProcessPayment(payment);
            await PublishOutcomeAsync(
                sendResult.Document,
                success: true,
                subject: PaymentRequestConstants.SuccessServiceBusSubject,
                tabaPayResponse: sendResult.Response,
                message: sendResult.RawResponse);
            return sendResult.Document;
        }
        catch (TabaPayProcessingException ex)
        {
            _logger.LogError(ex,
                "TabaPay failed for evolveId {EvolveId} — letting SB retry/DLQ handle the rest.",
                payment.EvolveId);

            await PublishOutcomeAsync(
                payment,
                success: false,
                subject: PaymentRequestConstants.FailureServiceBusSubject,
                tabaPayResponse: null,
                message: $"TabaPay error: {ex.Message}");
            throw;
        }
    }

    private async Task CheckLimitAsync(EvolvePaymentRequest payment, CancellationToken cancellationToken)
    {
        var limitResult = await _limitService.CheckAsync(BuildLimitRequest(payment), cancellationToken);
        if (limitResult.Allowed) return;

        _logger.LogWarning(
            "LimitService rejected payment {EvolveId}: {Reason}", payment.EvolveId, limitResult.Reason);
        throw new LimitExceededException(limitResult.Reason ?? "Limit check denied");
    }

    private async Task ReserveLedgerAsync(EvolvePaymentRequest payment, CancellationToken cancellationToken)
    {
        var ledgerResult = await _ledgerService.ReserveAsync(BuildLedgerRequest(payment), cancellationToken);
        if (!ledgerResult.Success)
        {
            _logger.LogWarning(
                "LedgerService failed to reserve for payment {EvolveId}: {Reason}",
                payment.EvolveId, ledgerResult.Reason);
            throw new LedgerReservationException(ledgerResult.Reason ?? "Ledger reservation denied");
        }

        _logger.LogInformation(
            "Ledger reservation {ReservationId} for evolveId {EvolveId}",
            ledgerResult.ReservationId, payment.EvolveId);
    }

    private static LimitCheckRequest BuildLimitRequest(EvolvePaymentRequest p) => new()
    {
        EvolveId = p.EvolveId,
        ClientId = p.ClientId ?? string.Empty,
        MerchantId = p.MerchantId ?? string.Empty,
        FintechId = p.FintechId ?? string.Empty,
        FboAccountNumber = p.FboAccountNumber ?? string.Empty,
        Amount = p.Amount ?? string.Empty,
        Currency = p.PaymentCurrency ?? string.Empty
    };

    private static LedgerReservationRequest BuildLedgerRequest(EvolvePaymentRequest p) => new()
    {
        EvolveId = p.EvolveId,
        ClientId = p.ClientId ?? string.Empty,
        MerchantId = p.MerchantId ?? string.Empty,
        FintechId = p.FintechId ?? string.Empty,
        FboAccountNumber = p.FboAccountNumber ?? string.Empty,
        Amount = p.Amount ?? string.Empty,
        Currency = p.PaymentCurrency ?? string.Empty,
        PaymentReference = p.PaymentReference
    };

    private async Task PublishOutcomeAsync(
        EvolvePaymentRequest payment,
        bool success,
        string subject,
        Models.Response.TabaPayResponse? tabaPayResponse,
        string message)
    {
        var envelope = ServiceBusHelper.CreateServiceBusMessage(
            payment,
            success: success,
            additionalInfo: new
            {
                payment.PaymentReference,
                Status = success
                    ? PaymentRequestConstants.TransactionCompleted
                    : PaymentRequestConstants.TransactionFailed,
                Message = message
            },
            comments: null);

        if (tabaPayResponse is not null)
            envelope.TabaPayResponse = tabaPayResponse;

        await _serviceBus.SendMessageToServiceBusAsync(envelope, subject);
    }
}
