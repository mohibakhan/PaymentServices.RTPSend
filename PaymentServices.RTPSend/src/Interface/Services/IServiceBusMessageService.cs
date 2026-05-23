using PaymentServices.RTPSend.Models;

namespace PaymentServices.RTPSend.Interface.Services;

public interface IServiceBusMessageService
{
    /// <summary>
    /// Wraps the envelope in a ServiceBusRequest and sends it to the configured
    /// rtpSend topic via <see cref="Interface.Adapters.IServiceBusAdapter"/>.
    /// Used for terminal outcome envelopes (success/failure of a payment).
    /// </summary>
    Task SendMessageToServiceBusAsync(ServiceBusContentModel serviceBusMessage, string subject);

    /// <summary>
    /// Publishes an arbitrary payload to a named queue/topic. Used by
    /// CreatePayment to enqueue the lightweight processing message.
    /// </summary>
    Task SendToQueueAsync<T>(T payload, string queueOrTopicName, string? subject = null) where T : notnull;
}
