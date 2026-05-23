using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models;
using PaymentServices.RTPSend.Settings;

namespace PaymentServices.RTPSend.Services;

public sealed class ServiceBusMessageService : IServiceBusMessageService
{
    private readonly RtpSendSettings _settings;
    private readonly IServiceBusAdapter _adapter;
    private readonly ILogger<ServiceBusMessageService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ServiceBusMessageService(
        IOptions<RtpSendSettings> settings,
        IServiceBusAdapter adapter,
        ILogger<ServiceBusMessageService> logger)
    {
        _settings = settings.Value;
        _adapter = adapter;
        _logger = logger;
    }

    public async Task SendMessageToServiceBusAsync(ServiceBusContentModel envelope, string subject)
    {
        var content = JsonSerializer.Serialize(envelope, _jsonOptions);
        _logger.LogInformation("Sending message to Service Bus: {Content}", content);

        var request = new ServiceBusRequest
        {
            Content = content,
            Subject = subject,
            QueueName = _settings.SERVICE_BUS_TOPIC_NAME
        };

        await _adapter.SendMessage(request);
    }

    public async Task SendToQueueAsync<T>(T payload, string queueOrTopicName, string? subject = null) where T : notnull
    {
        var content = JsonSerializer.Serialize(payload, _jsonOptions);
        _logger.LogInformation("Sending message to {Queue}: {Content}", queueOrTopicName, content);

        var request = new ServiceBusRequest
        {
            Content = content,
            Subject = subject ?? string.Empty,
            QueueName = queueOrTopicName
        };

        await _adapter.SendMessage(request);
    }
}
