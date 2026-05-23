using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models;

namespace PaymentServices.RTPSend.Services;

public sealed class ServiceBusMessageService : IServiceBusMessageService
{
    private readonly IServiceBusAdapter _adapter;
    private readonly ILogger<ServiceBusMessageService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ServiceBusMessageService(
        IServiceBusAdapter adapter,
        ILogger<ServiceBusMessageService> logger)
    {
        _adapter = adapter;
        _logger = logger;
    }

    public async Task SendMessageToServiceBusAsync(ServiceBusContentModel envelope, string subject)
    {
        var content = JsonSerializer.Serialize(envelope, _jsonOptions);
        _logger.LogInformation("Sending message to Service Bus topic {Topic} with subject {Subject}: {Content}",
            PaymentRequestConstants.ServiceBusTopicName, subject, content);

        var request = new ServiceBusRequest
        {
            Content = content,
            Subject = subject,
            QueueName = PaymentRequestConstants.ServiceBusTopicName
        };

        await _adapter.SendMessage(request);
    }

    public async Task SendToQueueAsync<T>(T payload, string queueOrTopicName, string? subject = null) where T : notnull
    {
        var content = JsonSerializer.Serialize(payload, _jsonOptions);
        _logger.LogInformation("Sending message to {Queue} with subject {Subject}: {Content}",
            queueOrTopicName, subject ?? "(none)", content);

        var request = new ServiceBusRequest
        {
            Content = content,
            Subject = subject ?? string.Empty,
            QueueName = queueOrTopicName
        };

        await _adapter.SendMessage(request);
    }
}
