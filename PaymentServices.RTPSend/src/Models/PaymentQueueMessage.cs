using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models;

/// <summary>
/// Lightweight envelope published by CreatePayment to the shared
/// <c>payment-processing</c> topic with Subject = "CreatePaymentRequest".
/// RTPSend's <c>rtpsend-process</c> subscription on that topic filters for
/// this subject and triggers ProcessPayment. The persisted Cosmos document
/// is the source of truth — ProcessPayment re-reads it by <see cref="EvolveId"/>
/// rather than trusting the message body.
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
