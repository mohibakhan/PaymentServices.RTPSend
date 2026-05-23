using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models.Domain;

/// <summary>
/// Request body sent to TabaPay's create-transaction endpoint.
/// Conversion from <see cref="Cosmos.EvolvePaymentRequest"/> is done by
/// <see cref="Helpers.TabaPayRequestHelper.ConvertEvolveToTabaPayRequest"/> —
/// we don't define implicit/explicit operators here.
/// </summary>
public class TabapayPaymentRequest
{
    [JsonPropertyName("referenceID")]
    [MinLength(1)]
    [MaxLength(15)]
    public string ReferenceId { get; set; } = string.Empty;

    [JsonPropertyName("corresponding")]
    public Corresponding? Corresponding { get; set; }

    /// <summary>"push" or "pull"</summary>
    [JsonPropertyName("type")]
    [MinLength(4)]
    [MaxLength(4)]
    public string Type { get; set; } = string.Empty;

    /// <summary>"R" = RTP</summary>
    [JsonPropertyName("achOptions")]
    public string AchOptions { get; set; } = string.Empty;

    [JsonPropertyName("accounts")]
    public Accounts? Accounts { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    /// <summary>
    /// Optional soft descriptor — TabaPay displays this on the cardholder's
    /// statement instead of the merchant's default descriptor.
    /// </summary>
    [JsonPropertyName("softDescriptor")]
    public SoftDescriptor? SoftDescriptor { get; set; }

    /// <summary>
    /// Free-text payment purpose. TabaPay propagates this into the outbound
    /// PACS.008 &lt;RmtInf&gt; block, which the receiving bank surfaces on
    /// the recipient's statement. Max 140 chars per ISO 20022.
    /// </summary>
    [JsonPropertyName("memo")]
    [MaxLength(140)]
    public string? Memo { get; set; }
}

public class Accounts
{
    /// <summary>22-character TabaPay account ID; use Settlement Account ID for push transactions.</summary>
    [JsonPropertyName("sourceAccountID")]
    [MinLength(22)]
    [MaxLength(22)]
    public string? SourceAccountId { get; set; }

    /// <summary>Use sourceAccount XOR sourceAccountID. sourceAccount valid on pull only.</summary>
    [JsonPropertyName("sourceAccount")]
    public Account? SourceAccount { get; set; }

    /// <summary>22-character TabaPay account ID; use Settlement Account ID for pull transactions.</summary>
    [JsonPropertyName("destinationAccountID")]
    [MinLength(22)]
    [MaxLength(22)]
    public string? DestinationAccountId { get; set; }

    /// <summary>Use destinationAccount XOR destinationAccountID. destinationAccount valid on push only.</summary>
    [JsonPropertyName("destinationAccount")]
    public Account? DestinationAccount { get; set; }
}

public class Account
{
    [JsonPropertyName("bank")]
    public Bank? Bank { get; set; }

    [JsonPropertyName("owner")]
    public Owner? Owner { get; set; }
}

public class Bank
{
    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("routingNumber")]
    public string? RoutingNumber { get; set; }

    [JsonPropertyName("accountType")]
    public string? AccountType { get; set; }
}

public class Owner
{
    [JsonPropertyName("name")]
    public AccountName? Name { get; set; }

    [JsonPropertyName("address")]
    public TabapayAddress? Address { get; set; }

    [JsonPropertyName("phone")]
    public Phone? Phone { get; set; }
}

public class Corresponding
{
    [JsonPropertyName("name")]
    public AccountName? Name { get; set; }

    [JsonPropertyName("address")]
    public TabapayAddress? Address { get; set; }

    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("sourceOfFunds")]
    public string? SourceOfFunds { get; set; }
}
