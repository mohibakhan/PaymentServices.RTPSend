using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models.Domain;

public class SourceAccount
{
    [JsonPropertyName("accountNumber")]
    [Description("Identification assigned by an institution.")]
    public string AccountNumber { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public AccountName Name { get; set; } = new();

    [JsonPropertyName("address")]
    [Description("Address. All fields optional")]
    public Address? Address { get; set; }

    [JsonPropertyName("routingNumber")]
    [Description("ABA routing transit number")]
    public string RoutingNumber { get; set; } = string.Empty;

    [JsonPropertyName("accountType")]
    [EnumDataType(typeof(AccountType))]
    [Description("Bank Account Type")]
    public string AccountType { get; set; } = string.Empty;

    [JsonPropertyName("debtorBankMemberID")]
    [Description("Debtor Bank MemberID")]
    public string? DebtorBankMemberID { get; set; }

    [JsonPropertyName("debtorIdOther")]
    public string? DebtorIdOther { get; set; }
}

public class DestinationAccount
{
    [JsonPropertyName("accountNumber")]
    [Description("Identification assigned by an institution.")]
    public string AccountNumber { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public AccountName Name { get; set; } = new();

    [JsonPropertyName("routingNumber")]
    [Description("ABA routing transit number")]
    public string RoutingNumber { get; set; } = string.Empty;

    [JsonPropertyName("accountType")]
    [EnumDataType(typeof(AccountType))]
    [Description("Bank Account Type. S - Savings, C - Checking, A - Business Savings, B - Business Checking, L - Loan")]
    public string AccountType { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    [Description("Address. All fields optional")]
    public Address? Address { get; set; }

    [JsonPropertyName("phoneNumber")]
    [Description("Phone Number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("creditorAgentTCHMemberID")]
    [Description("Creditor Agent TCH MemberID")]
    public string? CreditorAgentTCHMemberID { get; set; }

    [JsonPropertyName("creditorIdOther")]
    public string? CreditorIdOther { get; set; }
}
