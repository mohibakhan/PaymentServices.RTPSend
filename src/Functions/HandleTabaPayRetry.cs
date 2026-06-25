using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Settings;
using System.Text.Json;

namespace PaymentServices.RTPSend.Functions;

/// <summary>
/// Drains the dedicated <c>rtpsend-tabapay-retry</c> queue. Messages land here
/// (with a backoff delay) when a TabaPay send fails transiently, so the call is
/// retried later instead of redelivering the outcome message in an instant loop.
///
/// Each delivery re-loads the Cosmos doc, re-attempts the TabaPay send, and lets
/// <see cref="TabaPaySendFlow"/> decide the disposition: complete on success,
/// dead-letter on a non-retryable failure, or schedule the next backed-off retry
/// until <c>MaxTabaPayRetries</c> is reached (then dead-letter).
///
/// MANUAL SETTLE, like <see cref="HandlePaymentOutcome"/>.
/// </summary>
public class HandleTabaPayRetry
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IPaymentCosmosDBAdapter _paymentCosmosDB;
    private readonly ITabaPaySendService _tabaPay;
    private readonly IServiceBusMessageService _serviceBus;
    private readonly RtpSendSettings _settings;
    private readonly ILogger<HandleTabaPayRetry> _logger;

    public HandleTabaPayRetry(
        IPaymentCosmosDBAdapter paymentCosmosDB,
        ITabaPaySendService tabaPay,
        IServiceBusMessageService serviceBus,
        IOptions<RtpSendSettings> settings,
        ILogger<HandleTabaPayRetry> logger)
    {
        _paymentCosmosDB = paymentCosmosDB;
        _tabaPay = tabaPay;
        _serviceBus = serviceBus;
        _settings = settings.Value;
        _logger = logger;
    }

    [Function(nameof(HandleTabaPayRetry))]
    public async Task Run(
        [ServiceBusTrigger(
            queueName: PaymentRequestConstants.TabaPayRetryQueueName,
            Connection = "SERVICE_BUS_CONNSTRING")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        TabaPayRetryMessage? retry;
        try
        {
            retry = JsonSerializer.Deserialize<TabaPayRetryMessage>(message.Body.ToString(), _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Cannot deserialize TabaPay retry message {MessageId}; dead-lettering.", message.MessageId);
            await TabaPaySendFlow.DeadLetterAsync(messageActions, message, _logger,
                "DeserializeError", ex.Message, message.MessageId, cancellationToken);
            return;
        }

        if (retry is null || string.IsNullOrWhiteSpace(retry.EvolveId))
        {
            _logger.LogWarning(
                "TabaPay retry message {MessageId} has no evolveId; dead-lettering.", message.MessageId);
            await TabaPaySendFlow.DeadLetterAsync(messageActions, message, _logger,
                "MissingEvolveId", "Retry message had no evolveId", message.MessageId, cancellationToken);
            return;
        }

        _logger.LogInformation(
            "TabaPay retry received. EvolveId={EvolveId} Attempt={Attempt} DeliveryCount={DeliveryCount}",
            retry.EvolveId, retry.Attempt, message.DeliveryCount);

        try
        {
            var payment = (await _paymentCosmosDB.FindAllItemsAsync(retry.EvolveId)).FirstOrDefault();
            if (payment is null)
            {
                // The doc should exist (the original outcome found it). Dead-letter
                // for investigation — the ledger has already been debited.
                _logger.LogError(
                    "No payment doc for evolveId {EvolveId} on TabaPay retry; dead-lettering.", retry.EvolveId);
                await TabaPaySendFlow.DeadLetterAsync(messageActions, message, _logger,
                    "PaymentDocNotFound", $"No RTPSend payment doc for evolveId {retry.EvolveId}",
                    retry.EvolveId, cancellationToken);
                return;
            }

            // Idempotency / terminal guards — never re-send a settled payment.
            if (payment.Status == RequestStatus.COMPLETED.ToString())
            {
                _logger.LogInformation(
                    "Payment {EvolveId} already COMPLETED; completing retry message.", retry.EvolveId);
                await TabaPaySendFlow.CompleteAsync(messageActions, message, _logger, retry.EvolveId, cancellationToken);
                return;
            }

            if (payment.Status == RequestStatus.FAILED_TABAPAY.ToString()
                || payment.Status == RequestStatus.FAILED_NSF.ToString())
            {
                _logger.LogInformation(
                    "Payment {EvolveId} already terminal ({Status}); completing retry message.",
                    retry.EvolveId, payment.Status);
                await TabaPaySendFlow.CompleteAsync(messageActions, message, _logger, retry.EvolveId, cancellationToken);
                return;
            }

            TabaPaySendResult sendResult;
            try
            {
                sendResult = await _tabaPay.ProcessPayment(payment);
            }
            catch (TabaPayProcessingException ex)
            {
                await TabaPaySendFlow.HandleFailureAsync(
                    _serviceBus, _logger, _settings.MaxTabaPayRetries,
                    payment, ex, retry.Attempt, message, messageActions, cancellationToken);
                return;
            }

            _logger.LogInformation(
                "Payment {EvolveId} COMPLETED via TabaPay on retry attempt {Attempt}.", retry.EvolveId, retry.Attempt);
            await TabaPaySendFlow.PublishSuccessNotificationAsync(
                _serviceBus, _logger, sendResult.Document, sendResult.Response);
            await TabaPaySendFlow.CompleteAsync(messageActions, message, _logger, retry.EvolveId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error on TabaPay retry for evolveId {EvolveId}; abandoning.", retry.EvolveId);
            await TabaPaySendFlow.AbandonAsync(messageActions, message, _logger, retry.EvolveId, cancellationToken);
        }
    }
}
