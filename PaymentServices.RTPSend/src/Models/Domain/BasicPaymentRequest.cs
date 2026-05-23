using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models.Domain;

public class BasicPaymentRequest
{
    [JsonPropertyName("paymentReference")]
    [MaxLength(35)]
    [Description("Unique identification, as assigned by the original initiating party, to unambiguously identify the original transaction. Max 35 characters.")]
    public string PaymentReference { get; set; } = string.Empty;

    [JsonPropertyName("sourceAccountId")]
    public string? SourceAccountId { get; set; }

    [JsonPropertyName("sourceAccount")]
    [Description("Source Account Details")]
    public SourceAccount? SourceAccount { get; set; }

    [JsonPropertyName("destinationAccountId")]
    public string? DestinationAccountId { get; set; }

    [JsonPropertyName("destinationAccount")]
    [Description("Destination Account Details")]
    public DestinationAccount? DestinationAccount { get; set; }

    [JsonPropertyName("amount")]
    [MaxLength(18)]
    [Description("Restrictions of number of digits before and after decimal point based on currency selected")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("ultimateDebtor")]
    [Description("Ultimate Debtor")]
    public UltimateDebtor? UltimateDebtor { get; set; }

    [JsonPropertyName("sourceCurrency")]
    [Description("Identification of the currency in which the source account is held")]
    public string? SourceCurrency { get; set; }

    [JsonPropertyName("paymentCurrency")]
    [Description("Identification of the currency in which the destination account is held")]
    public string? PaymentCurrency { get; set; }

    [JsonPropertyName("softDescriptor")]
    public SoftDescriptor? SoftDescriptor { get; set; }
}
