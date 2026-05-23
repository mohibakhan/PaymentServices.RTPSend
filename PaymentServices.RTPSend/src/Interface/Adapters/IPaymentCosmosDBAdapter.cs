using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using PaymentServices.RTPSend.Models.Cosmos;

namespace PaymentServices.RTPSend.Interface.Adapters;

public interface IPaymentCosmosDBAdapter
{
    Task<EvolvePaymentRequest?> GetItemAsync(string id, string evolvePaymentId);

    Task<EvolvePaymentRequest?> CreateItemAsync(EvolvePaymentRequest evolvePaymentRequest);

    Task<EvolvePaymentRequest?> UpdateItemAsync(EvolvePaymentRequest evolvePaymentRequest);

    Task<EvolvePaymentRequest?> PatchItemAsync(
        EvolvePaymentRequest evolvePaymentRequest,
        List<PatchOperation> patchOperations);

    Task<List<EvolvePaymentRequest>> FindAllItemsAsync(string evolveId);

    Task<List<EvolvePaymentRequest>> FindAllItemsAsync(string evolveId, string documentType, string documentSubType);

    Task<List<EvolvePaymentRequest>> GetPayment(string evolveId, IHeaderDictionary headers);

    Task<List<EvolvePaymentRequest>> GetPaymentByReference(string paymentReference, IHeaderDictionary headers);
}
