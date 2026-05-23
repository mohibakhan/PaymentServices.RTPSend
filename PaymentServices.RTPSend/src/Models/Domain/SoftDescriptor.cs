using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models.Domain;

public class SoftDescriptor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    public Phone? Phone { get; set; }

    [JsonPropertyName("address")]
    public Address? Address { get; set; }
}
