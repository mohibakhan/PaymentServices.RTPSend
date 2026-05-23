using System.ComponentModel;
using System.Text.Json.Serialization;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Models.Cosmos;

/// <summary>
/// The persisted payment document in the rtpSend Cosmos container.
/// Partition key: <c>/evolveId</c>.
///
/// Extends <see cref="BasicPaymentRequest"/> with bookkeeping fields
/// (status history, downstream IDs, timestamps).
/// </summary>
public class EvolvePaymentRequest : BasicPaymentRequest
{
    public EvolvePaymentRequest()
    {
        Id = Guid.NewGuid().ToString();
        EvolveId = Guid.NewGuid().ToString();
        CreatedTimeStamp = DateTime.UtcNow.ToCosmosDateTime();
        ModifiedTimeStamp = DateTime.UtcNow.ToCosmosDateTime();
        Status = RequestStatus.RECEIVED.ToString();
        StatusHistory = new List<StatusHistory>();
    }

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("evolveId")]
    public string EvolveId { get; set; }

    [JsonPropertyName("createdTimeStamp")]
    public string CreatedTimeStamp { get; set; }

    [JsonPropertyName("modifiedTimeStamp")]
    public string ModifiedTimeStamp { get; set; }

    [JsonPropertyName("status")]
    public virtual string Status { get; set; }

    [JsonPropertyName("stage")]
    public virtual string? Stage { get; set; }

    [JsonPropertyName("documentType")]
    public virtual string? DocumentType { get; set; }

    [JsonPropertyName("documentSubType")]
    public virtual string? DocumentSubType { get; set; }

    [JsonPropertyName("instructionId")]
    public virtual string? InstructionId { get; set; }

    [JsonPropertyName("origInstructionId")]
    public virtual string? OrigInstructionId { get; set; }

    [JsonPropertyName("endToEndId")]
    public virtual string? EndToEndId { get; set; }

    [JsonPropertyName("quoteId")]
    public virtual string? QuoteId { get; set; }

    [JsonPropertyName("tranCode")]
    public virtual string? TranCode { get; set; }

    [JsonPropertyName("vendorDetails")]
    public virtual string? VendorDetails { get; set; }

    [JsonPropertyName("tabaPayTransactionId")]
    public virtual string? TabaPayTransactionId { get; set; }

    [JsonPropertyName("tabaPayReferenceId")]
    public virtual string? TabaPayReferenceId { get; set; }

    [JsonPropertyName("achOptions")]
    [Description("Will default to 'R' (RTP).")]
    public virtual string? AchOptions { get; set; }

    [JsonPropertyName("type")]
    [Description("PUSH or PULL")]
    public virtual string? Type { get; set; }

    [JsonPropertyName("trnRcptId")]
    [Description("TrnRcptId coming from JHA")]
    public virtual string? TrnRcptId { get; set; }

    [JsonPropertyName("clientId")]
    [Description("Client Id from header")]
    public virtual string? ClientId { get; set; }

    [JsonPropertyName("merchantId")]
    [Description("Merchant Id from header")]
    public virtual string? MerchantId { get; set; }

    [JsonPropertyName("gluId")]
    [Description("RTP Ledger Id (combined)")]
    public virtual string? GluId { get; set; }

    [JsonPropertyName("gluId_s")]
    [Description("Source Account RTP Ledger Id")]
    public virtual string? GluId_s { get; set; }

    [JsonPropertyName("gluId_d")]
    [Description("Destination Account RTP Ledger Id")]
    public virtual string? GluId_d { get; set; }

    [JsonPropertyName("fintechId")]
    public virtual string? FintechId { get; set; }

    [JsonPropertyName("statusHistory")]
    public virtual List<StatusHistory> StatusHistory { get; set; }

    [JsonPropertyName("valueDate")]
    [Description("The date on or before which the creditor must be credited.")]
    public virtual string? ValueDate { get; set; }

    [JsonPropertyName("fboAccount")]
    public virtual string? FboAccountNumber { get; set; }

    [JsonPropertyName("fboAccountName")]
    public virtual string? FboAccountName { get; set; }

    [JsonPropertyName("taxId")]
    public virtual string? TaxId { get; set; }

    [JsonPropertyName("userIsBusiness")]
    public virtual bool UserIsBusiness { get; set; }
}
