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

    /// <summary>
    /// When set, the message is scheduled to become visible at this time instead
    /// of being enqueued immediately (used for backed-off TabaPay retries).
    /// </summary>
    public DateTimeOffset? ScheduledEnqueueTime { get; set; }

    /// <summary>Optional custom application properties to stamp on the message.</summary>
    public IDictionary<string, object>? ApplicationProperties { get; set; }
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
