using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Interface;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Models.Response;
using PaymentServices.RTPSend.Settings;

namespace PaymentServices.RTPSend.Helpers;

public sealed class EvolvePaymentRequestHelper : IEvolvePaymentRequestHelper
{
    private readonly RtpSendSettings _settings;

    public EvolvePaymentRequestHelper(IOptions<RtpSendSettings> settings)
    {
        _settings = settings.Value;
    }

    public EvolvePaymentRequest ConvertBasicToEvolveRequest(
        BasicPaymentRequest basicPaymentRequest,
        IHeaderDictionary headers,
        string documentSubType)
    {
        var now = DateTime.UtcNow.ToCosmosDateTime();

        var evolvePaymentRequest = new EvolvePaymentRequest
        {
            Type = PaymentRequestConstants.CreatePaymentTypePush,
            AchOptions = PaymentRequestConstants.CreatePaymentAchOptions,
            PaymentCurrency = PaymentRequestConstants.CreatePaymentDefaultCurrency,
            ValueDate = now,
            TranCode = _settings.RTP_SEND_TRAN_CODE,
            CreatedTimeStamp = now,
            DocumentType = PaymentRequestConstants.DocumentType,
            DocumentSubType = documentSubType,
            ClientId = headers["x-client-id"].ToString(),
            MerchantId = headers["x-merchant-id"].ToString(),
            Status = RequestStatus.RECEIVED.ToString(),
            Stage = RequestStage.RTP_API.ToString()
        };

        evolvePaymentRequest.StatusHistory.Add(new StatusHistory
        {
            Stage = RequestStage.RTP_API.ToString(),
            StatusDate = now,
            Status = EnumHelper.GetEnumValue(RequestStatus.RECEIVED)
        });

        // Top-level payment fields
        evolvePaymentRequest.PaymentReference = basicPaymentRequest.PaymentReference.Trim();
        evolvePaymentRequest.SourceAccountId = basicPaymentRequest.SourceAccountId;
        evolvePaymentRequest.Amount = basicPaymentRequest.Amount;
        evolvePaymentRequest.UltimateDebtor = basicPaymentRequest.UltimateDebtor;

        // Source account
        if (basicPaymentRequest.SourceAccount is not null)
        {
            var src = basicPaymentRequest.SourceAccount;
            evolvePaymentRequest.SourceAccount = new SourceAccount
            {
                Name = new AccountName
                {
                    First = src.Name.First,
                    Last = src.Name.Last,
                    Company = src.Name.Company
                },
                DebtorIdOther = src.DebtorIdOther,
                AccountNumber = src.AccountNumber.Trim(),
                RoutingNumber = src.RoutingNumber.Trim(),
                AccountType = src.AccountType,
                DebtorBankMemberID = src.DebtorBankMemberID
            };
        }

        // Destination account
        if (basicPaymentRequest.DestinationAccount is not null)
        {
            var dst = basicPaymentRequest.DestinationAccount;
            evolvePaymentRequest.DestinationAccount = new DestinationAccount
            {
                Name = new AccountName
                {
                    First = dst.Name.First,
                    Last = dst.Name.Last,
                    Company = dst.Name.Company
                },
                Address = dst.Address is null ? null : new Address
                {
                    City = dst.Address.City,
                    CountryISOCode = dst.Address.CountryISOCode,
                    PostalCode = dst.Address.PostalCode,
                    StateCode = dst.Address.StateCode,
                    AddressLines = dst.Address.AddressLines
                },
                CreditorIdOther = dst.CreditorIdOther,
                AccountNumber = dst.AccountNumber.Trim(),
                RoutingNumber = dst.RoutingNumber.Trim(),
                AccountType = dst.AccountType,
                PhoneNumber = dst.PhoneNumber,
                CreditorAgentTCHMemberID = dst.CreditorAgentTCHMemberID
            };
        }

        if (basicPaymentRequest.SoftDescriptor is not null)
            evolvePaymentRequest.SoftDescriptor = basicPaymentRequest.SoftDescriptor;

        return evolvePaymentRequest;
    }

    // -------------------------------------------------------------------------
    // Patch-operation builders
    // -------------------------------------------------------------------------

    public static List<PatchOperation> GetStatusPatchOperation(
        RequestStage stage,
        RequestStatus status,
        object? additionalInfo = null)
    {
        var timestamp = DateTime.UtcNow.ToCosmosDateTime();
        return new List<PatchOperation>
        {
            PatchOperation.Add("/statusHistory/-", new StatusHistory
            {
                StatusDate = timestamp,
                Stage = stage.ToString(),
                Status = status.ToString(),
                AddInfo = additionalInfo
            }),
            PatchOperation.Replace("/stage", stage.ToString()),
            PatchOperation.Replace("/status", status.ToString()),
            PatchOperation.Replace("/modifiedTimeStamp", timestamp)
        };
    }

    public static List<PatchOperation> SetAccountLookupPatchoperation(PartnerLedgerResponse response) =>
        new()
        {
            PatchOperation.Replace("/fboAccount", response.FboAccount),
            PatchOperation.Replace("/fboAccountName", response.FboAccountName),
            PatchOperation.Replace("/fintechId", response.CifNo),
            PatchOperation.Replace("/taxId", response.TaxId),
            PatchOperation.Replace("/userIsBusiness", response.IsBusinessUser)
        };

    public static List<PatchOperation> GetTabaPaypatchoperation(
        string tabaPayTransactionId,
        string tabaPayReferenceId,
        string instructionId) =>
        new()
        {
            PatchOperation.Replace("/tabaPayTransactionId", tabaPayTransactionId),
            PatchOperation.Replace("/tabaPayReferenceId", tabaPayReferenceId),
            PatchOperation.Replace("/instructionId", instructionId)
        };

    public static List<PatchOperation> GetNodeUpdatePatchOperation(string nodeName, string nodeValue) =>
        new() { PatchOperation.Replace($"/{nodeName}", nodeValue) };
}
