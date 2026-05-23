using PaymentServices.RTPSend.Models.Response;

namespace PaymentServices.RTPSend.Interface.Adapters;

public interface IApiUserConfigCosmosAdapter
{
    Task<ApiUserConfigResponse?> GetApiUserConfigAsync(string clientId, string merchantId, string subscriptionKey);

    Task<ApiUserConfigResponse?> GetApiUserConfigAsync(string clientId, string merchantId);
}
