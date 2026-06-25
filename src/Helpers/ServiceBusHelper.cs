using PaymentServices.RTPSend.Models;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Helpers;

public static class ServiceBusHelper
{
    /// <summary>
    /// Projects the in-memory Cosmos document into the Service Bus envelope.
    /// AccountType is hardcoded (S/C) to match the legacy contract — downstream
    /// consumers expect those literal codes regardless of the source-of-truth.
    /// </summary>
    public static ServiceBusContentModel CreateServiceBusMessage(
        EvolvePaymentRequest cosmosDocument,
        bool success,
        object? additionalInfo,
        string? comments) =>
        new()
        {
            EvolveId = cosmosDocument.EvolveId,
            PaymentReference = cosmosDocument.PaymentReference,
            SourceAccount = cosmosDocument.SourceAccount is null ? null : new SourceAccount
            {
                AccountNumber = cosmosDocument.SourceAccount.AccountNumber,
                RoutingNumber = cosmosDocument.SourceAccount.RoutingNumber,
                Name = new AccountName
                {
                    First = cosmosDocument.SourceAccount.Name.First,
                    Last = cosmosDocument.SourceAccount.Name.Last,
                    Company = cosmosDocument.SourceAccount.Name.Company
                },
                AccountType = "S"
            },
            DestinationAccount = cosmosDocument.DestinationAccount is null ? null : new DestinationAccount
            {
                AccountNumber = cosmosDocument.DestinationAccount.AccountNumber,
                RoutingNumber = cosmosDocument.DestinationAccount.RoutingNumber,
                Name = new AccountName
                {
                    First = cosmosDocument.DestinationAccount.Name.First,
                    Last = cosmosDocument.DestinationAccount.Name.Last,
                    Company = cosmosDocument.DestinationAccount.Name.Company
                },
                AccountType = "C"
            },
            UltimateDebtor = cosmosDocument.UltimateDebtor is null ? null : new UltimateDebtor
            {
                Name = cosmosDocument.UltimateDebtor.Name
            },
            CIFNO = cosmosDocument.FintechId,
            SourceCurrency = cosmosDocument.PaymentCurrency ?? string.Empty,
            DestCurrency = cosmosDocument.PaymentCurrency ?? string.Empty,
            Status = cosmosDocument.Status,
            ValueDate = cosmosDocument.ValueDate,
            InstructionId = cosmosDocument.InstructionId,
            OriginalInstructionId = cosmosDocument.OrigInstructionId,
            PmtHandler = cosmosDocument.DocumentSubType,
            Amount = cosmosDocument.Amount,
            Success = success,
            DocumentType = cosmosDocument.DocumentType ?? string.Empty,
            AdditionalInfo = additionalInfo,
            Comments = comments,
            ClientId = cosmosDocument.ClientId,
            MerchantId = cosmosDocument.MerchantId
        };
}
