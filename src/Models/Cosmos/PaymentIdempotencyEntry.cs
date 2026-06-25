using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models.Cosmos;

/// <summary>
/// Cosmos document for atomic dedupe of <c>paymentReference</c>.
/// Container partition key: <c>/paymentReference</c>.
/// Lives in a dedicated <c>paymentIdempotency</c> container so we get
/// deterministic 409s on insert without affecting the partitioning of
/// the main paymentRequests container.
///
/// TTL is set so stale entries auto-clean; choose 90 days to comfortably
/// outlast any reasonable client retry window without keeping the
/// container indefinitely.
/// </summary>
public sealed class PaymentIdempotencyEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("paymentReference")]
    public string PaymentReference { get; set; } = string.Empty;

    [JsonPropertyName("evolveId")]
    public string EvolveId { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    /// Cosmos TTL in seconds. Container-level TTL must be enabled
    /// (DefaultTimeToLive >= 0) for this field to take effect.
    /// 90 days = 7_776_000 seconds.
    /// </summary>
    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 7_776_000;
}
