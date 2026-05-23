using System.Text.Json.Serialization;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Models.Response;

namespace PaymentServices.RTPSend.Models;

/// <summary>
/// Envelope published to Service Bus when a payment's lifecycle changes
/// (e.g. CreatePayment success/failure, TabaPay completion, fraud rejection).
///
/// Note: the JSON property <c>cifno</c> is intentionally lowercase to match
/// downstream consumers expecting the original Newtonsoft shape.
/// </summary>
public class ServiceBusContentModel
{
    [JsonPropertyName("evolveId")]
    public string EvolveId { get; set; } = string.Empty;

    [JsonPropertyName("paymentReference")]
    public string PaymentReference { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sourceAccount")]
    public SourceAccount? SourceAccount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("destinationAccount")]
    public DestinationAccount? DestinationAccount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("endToEndId")]
    public string? EndToEndId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("instructionId")]
    public string? InstructionId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("originalInstructionId")]
    public string? OriginalInstructionId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pmtHandler")]
    public string? PmtHandler { get; set; }

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("comments")]
    public string? Comments { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("valueDate")]
    public string? ValueDate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhookUrl")]
    public string? WebhookUrl { get; set; }

    [JsonPropertyName("sourceCurrency")]
    public string SourceCurrency { get; set; } = string.Empty;

    [JsonPropertyName("destinationCurrency")]
    public string DestCurrency { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cifno")]
    public string? CIFNO { get; set; }

    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("additionalInfo")]
    public object? AdditionalInfo { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ultimateDebtor")]
    public UltimateDebtor? UltimateDebtor { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("merchantId")]
    public string? MerchantId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tabaPayResponse")]
    public TabaPayResponse? TabaPayResponse { get; set; }
}
