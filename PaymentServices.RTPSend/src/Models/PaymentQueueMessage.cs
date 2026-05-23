using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models;

/// <summary>
/// Lightweight envelope published to <c>SERVICE_BUS_PROCESS_QUEUE_NAME</c> by
/// CreatePayment. The persisted Cosmos document is the source of truth —
/// ProcessPayment re-reads it by <see cref="EvolveId"/> rather than trusting
/// the message body.
/// </summary>
public sealed class PaymentQueueMessage
{
    [JsonPropertyName("evolveId")]
    public string EvolveId { get; init; } = string.Empty;

    [JsonPropertyName("paymentReference")]
    public string PaymentReference { get; init; } = string.Empty;

    [JsonPropertyName("enqueuedAt")]
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
}
