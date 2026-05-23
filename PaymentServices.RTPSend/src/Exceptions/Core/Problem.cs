using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Exceptions.Core;

public class Problem
{
    [JsonPropertyName("type")]
    public Uri Type { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    [JsonPropertyName("referenceCode")]
    public string ReferenceCode { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invalidParams")]
    public List<InvalidParams>? InvalidParams { get; set; }
}

public class InvalidParams
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string Details { get; set; } = string.Empty;
}
