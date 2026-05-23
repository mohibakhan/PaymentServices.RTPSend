using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models.Response;

public class CreatePaymentResponse
{
    [JsonPropertyName("evolveId")]
    public string EvolveId { get; set; } = string.Empty;

    [JsonPropertyName("paymentReference")]
    public string PaymentReference { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public class ApiUserConfigResponse
{
    [JsonPropertyName("fintechName")]
    public string FintechName { get; set; } = string.Empty;

    [JsonPropertyName("pmtHandler")]
    public string PmtHandler { get; set; } = string.Empty;

    [JsonPropertyName("notifyTo")]
    public string? NotifyTo { get; set; }

    [JsonPropertyName("notifyCc")]
    public string? NotifyCc { get; set; }

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("merchantId")]
    public string MerchantId { get; set; } = string.Empty;

    [JsonPropertyName("subscriptionKey")]
    public string SubscriptionKey { get; set; } = string.Empty;

    [JsonPropertyName("paymentReference")]
    public string? PaymentReference { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("pmtEventNotificationWebhookUrl")]
    public string? PmtEventNotificationWebhookUrl { get; set; }
}

public class TabaPayResponse
{
    [JsonPropertyName("SC")]
    public int Sc { get; set; }

    [JsonPropertyName("EC")]
    public string Ec { get; set; } = string.Empty;

    [JsonPropertyName("transactionID")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("network")]
    public string Network { get; set; } = string.Empty;

    [JsonPropertyName("networkRC")]
    public string NetworkRc { get; set; } = string.Empty;

    [JsonPropertyName("networkID")]
    public string NetworkId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("approvalCode")]
    public string ApprovalCode { get; set; } = string.Empty;
}

public class PartnerLedgerResponse
{
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

    /// <summary>True unless the legacy "0" sentinel is set.</summary>
    [JsonIgnore]
    public bool IsBusinessUser => UserIsBusiness != "0";

    [JsonPropertyName("accountStatus")]
    public string AccountStatus { get; set; } = string.Empty;

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

public class PrefundLedgerResponse
{
    [JsonPropertyName("evolveId")]
    public string EvolveId { get; set; } = string.Empty;

    [JsonPropertyName("fintechId")]
    public string FintechId { get; set; } = string.Empty;

    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("gluId")]
    public string GluId { get; set; } = string.Empty;

    [JsonPropertyName("gluId_s")]
    public string GluId_s { get; set; } = string.Empty;

    [JsonPropertyName("gluId_d")]
    public string GluId_d { get; set; } = string.Empty;

    [JsonPropertyName("statusCode")]
    public string? StatusCode { get; set; }
}

/// <summary>
/// Variable-shape error response from Prefund Ledger. <c>data</c> is sometimes
/// a string, sometimes a JSON object — we deserialize it as <see cref="JsonElement"/>
/// and let callers <c>.ToString()</c> for substring matching.
/// </summary>
public class PrefundErrorResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}
