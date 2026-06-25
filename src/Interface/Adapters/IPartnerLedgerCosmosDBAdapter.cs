using PaymentServices.RTPSend.Models.Request;
using PaymentServices.RTPSend.Models.Response;

namespace PaymentServices.RTPSend.Interface.Adapters;

public interface IPartnerLedgerCosmosDBAdapter
{
    Task<PartnerLedgerResponse> GetItemAsync(string accountNumber);

    Task<PartnerLedgerRequest?> CreateItemAsync(PartnerLedgerResponse partnerLedgerResponse);
}
