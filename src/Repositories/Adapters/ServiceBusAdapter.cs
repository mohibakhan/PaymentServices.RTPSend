using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Models;
using SharedAppSettings = PaymentServices.Shared.Models.AppSettings;

namespace PaymentServices.RTPSend.Repositories.Adapters;

/// <summary>
/// Sends ad-hoc messages to a Service Bus queue or topic named at call time.
/// Distinct from <c>PaymentServices.Shared.IServiceBusPublisher</c>, which is
/// opinionated about a typed PaymentMessage envelope.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class ServiceBusAdapter : IServiceBusAdapter, IAsyncDisposable
{
    private readonly SharedAppSettings _sharedSettings;
    private readonly ILogger<ServiceBusAdapter> _logger;

    private ServiceBusClient? _client;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public ServiceBusAdapter(
        IOptions<SharedAppSettings> sharedSettings,
        ILogger<ServiceBusAdapter> logger)
    {
        _sharedSettings = sharedSettings.Value;
        _logger = logger;
    }

    public async Task SendMessage(ServiceBusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.QueueName))
            throw new ArgumentException("QueueName is required.", nameof(request));

        try
        {
            var message = new ServiceBusMessage(request.Content)
            {
                Subject = request.Subject
            };

            if (request.ApplicationProperties is not null)
            {
                foreach (var (key, value) in request.ApplicationProperties)
                    message.ApplicationProperties[key] = value;
            }

            var sender = await GetOrCreateSenderAsync(request.QueueName);

            if (request.ScheduledEnqueueTime is { } enqueueAt)
                await sender.ScheduleMessageAsync(message, enqueueAt);
            else
                await sender.SendMessageAsync(message);
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex,
                "Error sending message to service bus. Queue: {Queue}, Subject: {Subject}, Reason: {Reason}",
                request.QueueName, request.Subject, ex.Reason);
            throw;
        }
    }

    private async Task<ServiceBusSender> GetOrCreateSenderAsync(string queueName)
    {
        if (_senders.TryGetValue(queueName, out var existing) && !existing.IsClosed)
            return existing;

        var client = await GetOrCreateClientAsync();
        var sender = client.CreateSender(queueName);
        _senders[queueName] = sender;
        return sender;
    }

    private async Task<ServiceBusClient> GetOrCreateClientAsync()
    {
        if (_client is not null && !_client.IsClosed)
            return _client;

        if (_client is not null && _client.IsClosed)
        {
            try { await _client.DisposeAsync(); } catch { /* swallow */ }
        }

        _client = new ServiceBusClient(_sharedSettings.SERVICE_BUS_CONNSTRING);
        return _client;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            try { await sender.DisposeAsync(); } catch { /* swallow */ }
        }
        _senders.Clear();

        if (_client is not null)
        {
            try { await _client.DisposeAsync(); } catch { /* swallow */ }
        }

        GC.SuppressFinalize(this);
    }
}
