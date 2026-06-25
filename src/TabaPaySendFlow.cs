using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Response;

namespace PaymentServices.RTPSend.Helpers;

/// <summary>
/// Shared settle/notify/retry logic for the TabaPay send step, used by both
/// <c>HandlePaymentOutcome</c> (first attempt, off the outcome message) and
/// <c>HandleTabaPayRetry</c> (backed-off retries, off the retry queue).
///
/// Failure disposition:
///   • non-retryable (4xx / hard decline) → notify + dead-letter immediately.
///   • retryable (5xx / timeout / network) → schedule a backed-off retry until
///     MaxTabaPayRetries is hit, then dead-letter. Never an instant abandon loop.
/// </summary>
public static class TabaPaySendFlow
{
    /// <summary>Capped exponential backoff for the Nth (1-based) retry attempt.</summary>
    public static TimeSpan Backoff(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(5),
        4 => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromMinutes(30),
    };

    /// <summary>
    /// Disposes of a failed TabaPay send: notify + dead-letter when terminal,
    /// or schedule the next backed-off retry and complete the current message.
    /// <paramref name="attempt"/> is the attempt that just failed (0 = first try
    /// off the outcome message; N = the Nth retry off the retry queue).
    /// </summary>
    public static async Task HandleFailureAsync(
        IServiceBusMessageService serviceBus,
        ILogger logger,
        int maxRetries,
        EvolvePaymentRequest payment,
        TabaPayProcessingException ex,
        int attempt,
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions actions,
        CancellationToken ct)
    {
        if (!ex.IsRetryable)
        {
            logger.LogWarning(ex,
                "TabaPay non-retryable failure for {EvolveId} (status {Status}); notifying + dead-lettering.",
                payment.EvolveId, ex.StatusCode);
            await PublishFailureNotificationAsync(serviceBus, logger, payment, ex.Message);
            await DeadLetterAsync(actions, message, logger, "TabaPayNonRetryable", ex.Message, payment.EvolveId, ct);
            return;
        }

        var nextAttempt = attempt + 1;
        if (nextAttempt > maxRetries)
        {
            logger.LogError(ex,
                "TabaPay still failing for {EvolveId} after {Attempts} retries; dead-lettering.",
                payment.EvolveId, attempt);
            await PublishFailureNotificationAsync(serviceBus, logger, payment,
                $"TabaPay transient failure, retries exhausted: {ex.Message}");
            await DeadLetterAsync(actions, message, logger, "TabaPayRetriesExhausted", ex.Message, payment.EvolveId, ct);
            return;
        }

        var delay = Backoff(nextAttempt);
        await serviceBus.SendToQueueAsync(
            new TabaPayRetryMessage { EvolveId = payment.EvolveId, Attempt = nextAttempt },
            PaymentRequestConstants.ServiceBusTopicName,
            subject: PaymentRequestConstants.TabaPaySendRetrySubject,
            delay: delay);

        logger.LogWarning(ex,
            "TabaPay transient failure for {EvolveId}; scheduled retry {Attempt}/{Max} in {Delay}.",
            payment.EvolveId, nextAttempt, maxRetries, delay);
        await CompleteAsync(actions, message, logger, payment.EvolveId, ct);
    }

    public static async Task PublishSuccessNotificationAsync(
        IServiceBusMessageService serviceBus, ILogger logger,
        EvolvePaymentRequest payment, TabaPayResponse? tabaPayResponse) =>
        await PublishNotificationAsync(serviceBus, logger, payment, success: true,
            PaymentRequestConstants.SuccessServiceBusSubject, tabaPayResponse, "Payment completed via TabaPay");

    public static Task PublishFailureNotificationAsync(
        IServiceBusMessageService serviceBus, ILogger logger,
        EvolvePaymentRequest payment, string? comments) =>
        PublishNotificationAsync(serviceBus, logger, payment, success: false,
            PaymentRequestConstants.FailureServiceBusSubject, tabaPayResponse: null, comments);

    /// <summary>Best-effort downstream notification — failures are logged, not thrown.</summary>
    public static async Task PublishNotificationAsync(
        IServiceBusMessageService serviceBus,
        ILogger logger,
        EvolvePaymentRequest payment,
        bool success,
        string subject,
        TabaPayResponse? tabaPayResponse,
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

            await serviceBus.SendMessageToServiceBusAsync(envelope, subject);

            logger.LogInformation(
                "Published '{Subject}' notification for EvolveId={EvolveId}.",
                subject, payment.EvolveId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to publish '{Subject}' notification for EvolveId={EvolveId}. " +
                "Payment is settled; notification will need manual replay.",
                subject, payment.EvolveId);
        }
    }

    // ---- Settle helpers (swallow settle errors so they don't cascade) -------

    public static async Task CompleteAsync(
        ServiceBusMessageActions actions, ServiceBusReceivedMessage msg, ILogger logger,
        string? evolveId, CancellationToken ct)
    {
        try { await actions.CompleteMessageAsync(msg, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "CompleteMessage failed (likely lock lost) for EvolveId={EvolveId}.", evolveId ?? "unknown");
        }
    }

    public static async Task AbandonAsync(
        ServiceBusMessageActions actions, ServiceBusReceivedMessage msg, ILogger logger,
        string? evolveId, CancellationToken ct)
    {
        try { await actions.AbandonMessageAsync(msg, cancellationToken: ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "AbandonMessage failed (likely lock lost) for EvolveId={EvolveId}.", evolveId ?? "unknown");
        }
    }

    public static async Task DeadLetterAsync(
        ServiceBusMessageActions actions, ServiceBusReceivedMessage msg, ILogger logger,
        string reason, string description, string? evolveId, CancellationToken ct)
    {
        try
        {
            await actions.DeadLetterMessageAsync(msg,
                deadLetterReason: reason, deadLetterErrorDescription: description, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "DeadLetter failed (likely lock lost) for EvolveId={EvolveId}.", evolveId ?? "unknown");
        }
    }
}
