using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models.Request;

/// <summary>
/// Cosmos document stored in the partner-ledger (fboAccounts) container.
/// Written by <c>PartnerLedgerCosmosDBAdapter.CreateItemAsync</c> after a successful
/// SQL stored-procedure lookup, so future lookups can hit Cosmos directly.
/// </summary>
public class PartnerLedgerRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("vAccountNumber")]
    public string VAccountNumber { get; set; } = string.Empty;

    [JsonPropertyName("fboAccount")]
    public string FboAccount { get; set; } = string.Empty;

    [JsonPropertyName("fboAccountName")]
    public string FboAccountName { get; set; } = string.Empty;

    [JsonPropertyName("CIFNO")]
    public string CifNo { get; set; } = string.Empty;

    [JsonPropertyName("taxId")]
    public string TaxId { get; set; } = string.Empty;

    [JsonPropertyName("userIsBusiness")]
    public string UserIsBusiness { get; set; } = string.Empty;

    [JsonPropertyName("accountStatus")]
    public string AccountStatus { get; set; } = string.Empty;

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
