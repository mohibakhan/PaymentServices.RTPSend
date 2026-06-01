using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.Shared.Enums;
using PaymentServices.Shared.Messages;

namespace PaymentServices.RTPSend.Functions;

/// <summary>
/// Service Bus-triggered outcome handler. Subscribed to the rtpsend-outcome
/// subscription on the shared payment-processing topic. The subscription filter
/// (set in infra) matches the terminal pipeline states:
///   TransferCompleted, TransferFailed, AccountResolutionFailed.
///
/// On TransferCompleted → load the RTPSend payment doc and call TabaPay.
/// On TransferFailed / AccountResolutionFailed → mark the payment terminally
/// failed (FAILED, or FAILED_NSF when the failure reason indicates NSF).
///
/// The pipeline (Gateway → AccountResolution → Transfer) handles ledger/limits/
/// screening. RTPSend re-enters here only to run TabaPay (which stayed in
/// RTPSend) and to record the terminal status on its own document.
/// </summary>
public class HandlePaymentOutcome
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IPaymentCosmosDBAdapter _paymentCosmosDB;
    private readonly ITabaPaySendService _tabaPay;
    private readonly ILogger<HandlePaymentOutcome> _logger;

    public HandlePaymentOutcome(
        IPaymentCosmosDBAdapter paymentCosmosDB,
        ITabaPaySendService tabaPay,
        ILogger<HandlePaymentOutcome> logger)
    {
        _paymentCosmosDB = paymentCosmosDB;
        _tabaPay = tabaPay;
        _logger = logger;
    }

    [Function(nameof(HandlePaymentOutcome))]
    public async Task Run(
        [ServiceBusTrigger(
            topicName: "payment-processing",
            subscriptionName: "rtpsend-outcome",
            Connection = "SERVICE_BUS_CONNSTRING")]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        PaymentMessage? outcome;
        try
        {
            outcome = ServiceBusPublisher.Deserialize(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Cannot deserialize outcome message {MessageId}; will retry then DLQ.",
                message.MessageId);
            throw;
        }

        _logger.LogInformation(
            "Outcome received. EvolveId={EvolveId} State={State}",
            outcome.EvolveId, outcome.State);

        switch (outcome.State)
        {
            case TransactionState.TransferCompleted:
                await HandleSuccessAsync(outcome, cancellationToken);
                break;

            case TransactionState.TransferFailed:
            case TransactionState.AccountResolutionFailed:
                await HandleFailureAsync(outcome, cancellationToken);
                break;

            default:
                // The subscription filter shouldn't deliver anything else, but
                // guard anyway so an unexpected state doesn't loop on retries.
                _logger.LogWarning(
                    "Ignoring unexpected outcome state {State} for evolveId {EvolveId}.",
                    outcome.State, outcome.EvolveId);
                break;
        }
    }

    private async Task HandleSuccessAsync(PaymentMessage outcome, CancellationToken cancellationToken)
    {
        var payment = await _paymentCosmosDB.GetItemAsync(outcome.EvolveId, outcome.EvolveId);
        if (payment is null)
        {
            _logger.LogError(
                "TransferCompleted but no RTPSend payment doc for evolveId {EvolveId}.",
                outcome.EvolveId);
            throw new InvalidOperationException(
                $"Payment document not found for evolveId {outcome.EvolveId}");
        }

        // Idempotency — if TabaPay already completed for this doc, don't repeat.
        if (payment.Status == RequestStatus.COMPLETED.ToString())
        {
            _logger.LogInformation(
                "Payment {EvolveId} already COMPLETED; skipping TabaPay.", outcome.EvolveId);
            return;
        }

        _logger.LogInformation("Transfer succeeded; calling TabaPay for {EvolveId}.", outcome.EvolveId);

        // TabaPayProcessingException bubbles up → SB retries, then DLQ.
        var sendResult = await _tabaPay.ProcessPayment(payment);

        var patches = EvolvePaymentRequestHelper.GetStatusPatchOperation(
            RequestStage.TABAPAY,
            RequestStatus.COMPLETED,
            additionalInfo: new
            {
                Message = "TabaPay completed",
                TabaPayTransactionId = sendResult.Document.TabaPayTransactionId
            });

        await _paymentCosmosDB.PatchItemAsync(payment, patches);

        _logger.LogInformation("Payment {EvolveId} COMPLETED via TabaPay.", outcome.EvolveId);
    }

    private async Task HandleFailureAsync(PaymentMessage outcome, CancellationToken cancellationToken)
    {
        var payment = await _paymentCosmosDB.GetItemAsync(outcome.EvolveId, outcome.EvolveId);
        if (payment is null)
        {
            _logger.LogError(
                "Failure outcome ({State}) but no RTPSend payment doc for evolveId {EvolveId}.",
                outcome.State, outcome.EvolveId);
            return; // nothing to update; complete the message
        }

        // NSF surfaces as a terminal, distinct status; everything else FAILED.
        var isNsf = outcome.FailureReason is not null &&
                    outcome.FailureReason.Contains("insufficient", StringComparison.OrdinalIgnoreCase);

        var status = isNsf ? RequestStatus.FAILED_NSF : RequestStatus.FAILED;

        var patches = EvolvePaymentRequestHelper.GetStatusPatchOperation(
            RequestStage.ACCOUNTLOOKUP,
            status,
            additionalInfo: new
            {
                Message = $"Pipeline failure: {outcome.State}",
                Reason = outcome.FailureReason
            });

        await _paymentCosmosDB.PatchItemAsync(payment, patches);

        _logger.LogWarning(
            "Payment {EvolveId} marked {Status} ({State}): {Reason}",
            outcome.EvolveId, status, outcome.State, outcome.FailureReason);
    }
}
