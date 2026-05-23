using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models.Domain;

public class StatusHistory
{
    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("statusDate")]
    public string StatusDate { get; set; } = string.Empty;

    /// <summary>
    /// Variable-shape: sometimes a string, sometimes an object.
    /// Stored as object? — System.Text.Json materializes it as JsonElement
    /// or string at deserialization time.
    /// </summary>
    [JsonPropertyName("addInfo")]
    public object? AddInfo { get; set; }
}
