using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models;

/// <summary>
/// Used by <see cref="Interface.Adapters.IServiceBusAdapter"/> to send arbitrary
/// JSON content to a named queue or topic.
/// </summary>
public class ServiceBusRequest
{
    public string Content { get; set; } = string.Empty;
    public string QueueName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
}

public class ServiceErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("additionalInfo")]
    public object? AddInfo { get; set; }
}
