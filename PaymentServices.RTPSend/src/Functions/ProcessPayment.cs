using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Models;
using PaymentServices.RTPSend.Services;

namespace PaymentServices.RTPSend.Functions;

/// <summary>
/// Service Bus-triggered worker. Consumes <see cref="PaymentQueueMessage"/>
/// instances from RTPSend's subscription on the shared <c>payment-processing</c>
/// topic. The subscription filter (set up in bicep) matches only messages
/// with Subject = "CreatePaymentRequest" so outcome envelopes for other
/// services are ignored.
///
/// Behavior on failure: any exception bubbles up, SB increments the message's
/// delivery count, and after MaxDeliveryCount the message is automatically
/// dead-lettered. The <c>RetryFailedPayments</c> timer drains that DLQ.
/// </summary>
public class ProcessPayment
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IPaymentCosmosDBAdapter _paymentCosmosDB;
    private readonly IPaymentOrchestrator _orchestrator;
    private readonly ILogger<ProcessPayment> _logger;

    public ProcessPayment(
        IPaymentCosmosDBAdapter paymentCosmosDB,
        IPaymentOrchestrator orchestrator,
        ILogger<ProcessPayment> logger)
    {
        _paymentCosmosDB = paymentCosmosDB;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [Function(nameof(ProcessPayment))]
    public async Task Run(
        [ServiceBusTrigger(
            topicName: "%rtpSend:AppSettings:SERVICE_BUS_TOPIC_NAME%",
            subscriptionName: "%rtpSend:AppSettings:SERVICE_BUS_PROCESS_SUBSCRIPTION_NAME%",
            Connection = "rtpSend:AppSettings:SERVICE_BUS_CONNSTRING")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        var body = message.Body.ToString();
        _logger.LogInformation("ProcessPayment received message {MessageId}: {Body}",
            message.MessageId, body);

        PaymentQueueMessage? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PaymentQueueMessage>(body, _jsonOptions);
        }
        catch (JsonException ex)
        {
            // Permanently malformed payload — re-throwing would just put it on the DLQ
            // eventually anyway, but with a useful error. We let SB handle the retry.
            _logger.LogError(ex, "Cannot deserialize message {MessageId}; will retry then DLQ.",
                message.MessageId);
            throw;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.EvolveId))
        {
            _logger.LogError("Message {MessageId} missing evolveId; throwing to DLQ.", message.MessageId);
            throw new InvalidOperationException($"Message {message.MessageId} missing evolveId");
        }

        // Source of truth = the persisted Cosmos document.
        var payment = (await _paymentCosmosDB.FindAllItemsAsync(envelope.EvolveId)).FirstOrDefault();
        if (payment is null)
        {
            _logger.LogError(
                "No payment document for evolveId {EvolveId} (paymentReference {Ref}); throwing to DLQ.",
                envelope.EvolveId, envelope.PaymentReference);
            throw new InvalidOperationException(
                $"No payment document found for evolveId {envelope.EvolveId}");
        }

        _logger.LogInformation("Processing payment evolveId {EvolveId}", envelope.EvolveId);
        await _orchestrator.ProcessAsync(payment, cancellationToken);
        _logger.LogInformation("Processing completed for evolveId {EvolveId}", envelope.EvolveId);
    }
}
