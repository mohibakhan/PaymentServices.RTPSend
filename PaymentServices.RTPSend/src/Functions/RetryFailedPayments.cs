using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Models;
using PaymentServices.RTPSend.Services;
using SharedAppSettings = PaymentServices.Shared.Models.AppSettings;

namespace PaymentServices.RTPSend.Functions;

/// <summary>
/// Timer-triggered DLQ drain for RTPSend's subscription on the shared
/// <c>payment-processing</c> topic. For each dead-lettered message:
///   1. Read the envelope to get the evolveId
///   2. Fetch the persisted Cosmos document
///   3. Hand to <see cref="IPaymentOrchestrator.ResumeFromAsync"/>, which
///      inspects payment.Stage and resumes from the failed step only
///   4. Complete the DLQ message on success; abandon on transient failure
///
/// Already-completed payments are completed without re-processing.
/// </summary>
public class RetryFailedPayments
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IPaymentCosmosDBAdapter _paymentCosmosDB;
    private readonly IPaymentOrchestrator _orchestrator;
    private readonly SharedAppSettings _sharedSettings;
    private readonly ILogger<RetryFailedPayments> _logger;

    public RetryFailedPayments(
        IPaymentCosmosDBAdapter paymentCosmosDB,
        IPaymentOrchestrator orchestrator,
        IOptions<SharedAppSettings> sharedSettings,
        ILogger<RetryFailedPayments> logger)
    {
        _paymentCosmosDB = paymentCosmosDB;
        _orchestrator = orchestrator;
        _sharedSettings = sharedSettings.Value;
        _logger = logger;
    }

    [Function(nameof(RetryFailedPayments))]
    public async Task Run(
        [TimerTrigger("%rtpSend:AppSettings:RETRY_TIMER_SCHEDULE%")] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("RetryFailedPayments tick at {Now}. Next: {Next}",
            DateTime.UtcNow, timerInfo.ScheduleStatus?.Next);

        await using var sbClient = new ServiceBusClient(_sharedSettings.SERVICE_BUS_CONNSTRING);

        // RTPSend's subscription dead-letter sub-queue on the shared topic.
        // Path is: {topic}/Subscriptions/{subscription}/$DeadLetterQueue
        await using var receiver = sbClient.CreateReceiver(
            PaymentRequestConstants.ServiceBusTopicName,
            PaymentRequestConstants.ServiceBusProcessSubscriptionName,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.DeadLetter
            });

        const int maxMessages = 32;
        var batch = await receiver.ReceiveMessagesAsync(maxMessages, TimeSpan.FromSeconds(10), cancellationToken);

        if (batch is null || batch.Count == 0)
        {
            _logger.LogInformation("DLQ empty — no payments to retry.");
            return;
        }

        _logger.LogInformation("Found {Count} dead-lettered payment messages.", batch.Count);

        foreach (var sbMessage in batch)
            await ProcessOneAsync(sbMessage, receiver, cancellationToken);
    }

    private async Task ProcessOneAsync(
        ServiceBusReceivedMessage sbMessage,
        ServiceBusReceiver receiver,
        CancellationToken cancellationToken)
    {
        var body = sbMessage.Body.ToString();
        _logger.LogInformation("Retry — message id {MessageId}, body: {Body}", sbMessage.MessageId, body);

        PaymentQueueMessage? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PaymentQueueMessage>(body, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Cannot deserialize DLQ message {MessageId}; completing to discard.", sbMessage.MessageId);
            await receiver.CompleteMessageAsync(sbMessage, cancellationToken);
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.EvolveId))
        {
            _logger.LogWarning("DLQ message {MessageId} has no evolveId; completing to discard.",
                sbMessage.MessageId);
            await receiver.CompleteMessageAsync(sbMessage, cancellationToken);
            return;
        }

        var payment = (await _paymentCosmosDB.FindAllItemsAsync(envelope.EvolveId)).FirstOrDefault();
        if (payment is null)
        {
            _logger.LogWarning("No Cosmos document for evolveId {EvolveId}; completing DLQ message {MessageId}.",
                envelope.EvolveId, sbMessage.MessageId);
            await receiver.CompleteMessageAsync(sbMessage, cancellationToken);
            return;
        }

        try
        {
            _logger.LogInformation(
                "Resuming evolveId {EvolveId} from stage {Stage}, status {Status}",
                envelope.EvolveId, payment.Stage, payment.Status);

            await _orchestrator.ResumeFromAsync(payment, cancellationToken);

            _logger.LogInformation("Resume succeeded for evolveId {EvolveId}", envelope.EvolveId);
            await receiver.CompleteMessageAsync(sbMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Resume failed for evolveId {EvolveId}; abandoning DLQ message {MessageId} for next cycle.",
                envelope.EvolveId, sbMessage.MessageId);
            await receiver.AbandonMessageAsync(sbMessage, cancellationToken: cancellationToken);
        }
    }
}
