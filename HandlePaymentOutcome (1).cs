using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.Shared.Enums;
using PaymentServices.Shared.Messages;

namespace PaymentServices.RTPSend.Functions;

/// <summary>
/// Service Bus-triggered outcome handler. Subscribed to the rtpsend-outcome
/// subscription on the shared payment-processing topic. Filter matches:
///   TransferCompleted, TransferFailed, AccountResolutionFailed.
///
/// On TransferCompleted → load the RTPSend payment doc and call TabaPay.
/// On TransferFailed / AccountResolutionFailed → mark the payment terminally
/// failed (FAILED, or FAILED_NSF when the failure reason indicates NSF).
///
/// MANUAL SETTLE: this function takes explicit control of the Service Bus
/// message via ServiceBusMessageActions (Complete / Abandon / DeadLetter)
/// instead of relying on auto-complete. Auto-complete settles the message
/// based on whether the function returns vs throws — which is fragile at host
/// scale-down and was observed losing messages at the tail of a load run
/// (TransferCompleted published + ledger debited, but the outcome message
/// vanished with no DLQ trace). Explicit settle removes that ambiguity.
///
/// READ-YOUR-OWN-WRITE: the payment doc is written by RTPSend's own
/// ProcessPayment moments earlier; the cross-partition lookup here can miss a
/// very recently written doc under load. On a miss we ABANDON (redeliver) so a
/// later attempt finds it, rather than throwing/dropping — only dead-lettering
/// after enough delivery attempts.
/// </summary>
public class HandlePaymentOutcome
{
    // Abandon-and-retry budget for the read-your-own-write race before we
    // give up and dead-letter (so a genuinely missing doc can't loop forever).
    private const int MaxLookupDeliveryAttempts = 5;

    private readonly IPaymentCosmosDBAdapter _paymentCosmosDB;
    private readonly ITabaPaySendService _tabaPay;
    private readonly IServiceBusMessageService _serviceBus;
    private readonly ILogger<HandlePaymentOutcome> _logger;

    public HandlePaymentOutcome(
        IPaymentCosmosDBAdapter paymentCosmosDB,
        ITabaPaySendService tabaPay,
        IServiceBusMessageService serviceBus,
        ILogger<HandlePaymentOutcome> logger)
    {
        _paymentCosmosDB = paymentCosmosDB;
        _tabaPay = tabaPay;
        _serviceBus = serviceBus;
        _logger = logger;
    }

    [Function(nameof(HandlePaymentOutcome))]
    public async Task Run(
        [ServiceBusTrigger(
            topicName: "payment-processing",
            subscriptionName: "rtpsend-outcome",
            Connection = "SERVICE_BUS_CONNSTRING")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        PaymentMessage? outcome;
        try
        {
            outcome = ServiceBusPublisher.Deserialize(message);
        }
        catch (Exception ex)
        {
            // Unparseable — no point retrying. Dead-letter explicitly.
            _logger.LogError(ex,
                "Cannot deserialize outcome message {MessageId}; dead-lettering.",
                message.MessageId);
            await SafeDeadLetterAsync(messageActions, message,
                "DeserializeError", ex.Message, message.MessageId, cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Outcome received. EvolveId={EvolveId} State={State} DeliveryCount={DeliveryCount}",
            outcome.EvolveId, outcome.State, message.DeliveryCount);

        try
        {
            switch (outcome.State)
            {
                case TransactionState.TransferCompleted:
                    await HandleSuccessAsync(outcome, message, messageActions, cancellationToken);
                    break;

                case TransactionState.TransferFailed:
                case TransactionState.AccountResolutionFailed:
                    await HandleFailureAsync(outcome, message, messageActions, cancellationToken);
                    break;

                default:
                    // Filter shouldn't deliver anything else; complete so it
                    // doesn't loop.
                    _logger.LogWarning(
                        "Ignoring unexpected outcome state {State} for evolveId {EvolveId}.",
                        outcome.State, outcome.EvolveId);
                    await SafeCompleteAsync(messageActions, message, outcome.EvolveId, cancellationToken);
                    break;
            }
        }
        catch (TabaPayProcessingException ex)
        {
            // Transient TabaPay failure — abandon so SB redelivers (the HTTP
            // resilience handler already retried within the call). Let delivery
            // count + maxDeliveryCount drive eventual DLQ.
            _logger.LogWarning(ex,
                "TabaPay failed for evolveId {EvolveId}; abandoning for redelivery (DeliveryCount={DeliveryCount}).",
                outcome.EvolveId, message.DeliveryCount);
            await SafeAbandonAsync(messageActions, message, outcome.EvolveId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Unexpected — abandon for redelivery; SB will DLQ after maxDeliveryCount.
            _logger.LogError(ex,
                "Unexpected error handling outcome for evolveId {EvolveId}; abandoning.",
                outcome.EvolveId);
            await SafeAbandonAsync(messageActions, message, outcome.EvolveId, cancellationToken);
        }
    }

    private async Task HandleSuccessAsync(
        PaymentMessage outcome,
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        var payment = await LookupPaymentAsync(outcome.EvolveId);

        if (payment is null)
        {
            // Read-your-own-write race: the doc should exist (ProcessPayment
            // wrote it). Abandon so a later delivery finds it — unless we've
            // tried enough times, in which case dead-letter for investigation.
            await HandleMissingDocAsync(outcome, message, messageActions, cancellationToken);
            return;
        }

        // Idempotency — if TabaPay already completed for this doc, don't repeat.
        if (payment.Status == RequestStatus.COMPLETED.ToString())
        {
            _logger.LogInformation(
                "Payment {EvolveId} already COMPLETED; completing message.", outcome.EvolveId);
            await SafeCompleteAsync(messageActions, message, outcome.EvolveId, cancellationToken);
            return;
        }

        _logger.LogInformation("Transfer succeeded; calling TabaPay for {EvolveId}.", outcome.EvolveId);

        // Throws TabaPayProcessingException on failure → caught in Run → abandon.
        var sendResult = await _tabaPay.ProcessPayment(payment);

        _logger.LogInformation("Payment {EvolveId} COMPLETED via TabaPay.", outcome.EvolveId);

        // Best-effort downstream notification (webhook caller, other team).
        await PublishNotificationAsync(
            sendResult.Document,
            success: true,
            subject: PaymentRequestConstants.SuccessServiceBusSubject,
            tabaPayResponse: sendResult.Response,
            comments: "Payment completed via TabaPay");

        // Work done — settle explicitly.
        await SafeCompleteAsync(messageActions, message, outcome.EvolveId, cancellationToken);
    }

    private async Task HandleFailureAsync(
        PaymentMessage outcome,
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        var payment = await LookupPaymentAsync(outcome.EvolveId);

        if (payment is null)
        {
            await HandleMissingDocAsync(outcome, message, messageActions, cancellationToken);
            return;
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

        var patched = await _paymentCosmosDB.PatchItemAsync(payment, patches) ?? payment;

        _logger.LogWarning(
            "Payment {EvolveId} marked {Status} ({State}): {Reason}",
            outcome.EvolveId, status, outcome.State, outcome.FailureReason);

        await PublishNotificationAsync(
            patched,
            success: false,
            subject: PaymentRequestConstants.FailureServiceBusSubject,
            tabaPayResponse: null,
            comments: outcome.FailureReason ?? $"Pipeline failure: {outcome.State}");

        await SafeCompleteAsync(messageActions, message, outcome.EvolveId, cancellationToken);
    }

    /// <summary>
    /// Loads the RTPSend payment document by evolveId. The doc's id != evolveId
    /// (the ctor generates them independently) and the container is partitioned
    /// by /evolveId, so this is a query (FindAllItemsAsync), not a point read.
    /// </summary>
    private async Task<EvolvePaymentRequest?> LookupPaymentAsync(string evolveId)
        => (await _paymentCosmosDB.FindAllItemsAsync(evolveId)).FirstOrDefault();

    /// <summary>
    /// Doc not found. Under load this is usually a read-your-own-write race
    /// (the doc was written moments ago by ProcessPayment but the query hit a
    /// not-yet-consistent replica). Abandon to force redelivery so a later
    /// attempt finds it. Only after MaxLookupDeliveryAttempts do we dead-letter
    /// (a genuinely missing doc), so we never silently drop the message —
    /// important because the ledger has already been debited at this point.
    /// </summary>
    private async Task HandleMissingDocAsync(
        PaymentMessage outcome,
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        if (message.DeliveryCount < MaxLookupDeliveryAttempts)
        {
            _logger.LogWarning(
                "Payment doc not yet found for evolveId {EvolveId} (DeliveryCount={DeliveryCount}); " +
                "abandoning for redelivery (read-your-own-write race).",
                outcome.EvolveId, message.DeliveryCount);
            await SafeAbandonAsync(messageActions, message, outcome.EvolveId, cancellationToken);
        }
        else
        {
            _logger.LogError(
                "Payment doc still not found for evolveId {EvolveId} after {Attempts} attempts; " +
                "dead-lettering (ledger may have been debited — needs reconciliation).",
                outcome.EvolveId, message.DeliveryCount);
            await SafeDeadLetterAsync(messageActions, message,
                "PaymentDocNotFound",
                $"No RTPSend payment doc for evolveId {outcome.EvolveId} after {message.DeliveryCount} attempts",
                outcome.EvolveId, cancellationToken);
        }
    }

    private async Task PublishNotificationAsync(
        EvolvePaymentRequest payment,
        bool success,
        string subject,
        Models.Response.TabaPayResponse? tabaPayResponse,
        string? comments)
    {
        try
        {
            var envelope = ServiceBusHelper.CreateServiceBusMessage(
                payment,
                success: success,
                additionalInfo: new
                {
                    payment.PaymentReference,
                    Status = payment.Status
                },
                comments: comments);

            if (tabaPayResponse is not null)
                envelope.TabaPayResponse = tabaPayResponse;

            await _serviceBus.SendMessageToServiceBusAsync(envelope, subject);

            _logger.LogInformation(
                "Published '{Subject}' notification for EvolveId={EvolveId}.",
                subject, payment.EvolveId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish '{Subject}' notification for EvolveId={EvolveId}. " +
                "Payment is settled; notification will need manual replay.",
                subject, payment.EvolveId);
        }
    }

    // ---- Settle helpers (swallow settle errors so they don't cascade) -------

    private async Task SafeCompleteAsync(
        ServiceBusMessageActions actions, ServiceBusReceivedMessage msg,
        string? evolveId, CancellationToken ct)
    {
        try { await actions.CompleteMessageAsync(msg, ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CompleteMessage failed (likely lock lost) for EvolveId={EvolveId}.", evolveId ?? "unknown");
        }
    }

    private async Task SafeAbandonAsync(
        ServiceBusMessageActions actions, ServiceBusReceivedMessage msg,
        string? evolveId, CancellationToken ct)
    {
        try { await actions.AbandonMessageAsync(msg, cancellationToken: ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AbandonMessage failed (likely lock lost) for EvolveId={EvolveId}.", evolveId ?? "unknown");
        }
    }

    private async Task SafeDeadLetterAsync(
        ServiceBusMessageActions actions, ServiceBusReceivedMessage msg,
        string reason, string description, string? evolveId, CancellationToken ct)
    {
        try
        {
            await actions.DeadLetterMessageAsync(msg,
                deadLetterReason: reason, deadLetterErrorDescription: description, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DeadLetter failed (likely lock lost) for EvolveId={EvolveId}.", evolveId ?? "unknown");
        }
    }
}
