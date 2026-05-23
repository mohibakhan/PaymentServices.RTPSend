using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Models.Response;

namespace PaymentServices.RTPSend.Repositories;

[ExcludeFromCodeCoverage]
public sealed class ApiUserConfigAdapter : IApiUserConfigCosmosAdapter
{
    private readonly Container _container;
    private readonly ILogger<ApiUserConfigAdapter> _logger;

    public ApiUserConfigAdapter(
        [FromKeyedServices("apiUserConfig")] Container container,
        ILogger<ApiUserConfigAdapter> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<ApiUserConfigResponse?> GetApiUserConfigAsync(string clientId, string merchantId, string subscriptionKey)
    {
        try
        {
            var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.clientId = @clientId AND c.merchantId = @merchantId AND c.subscriptionKey = @subscriptionKey")
                .WithParameter("@clientId", clientId)
                .WithParameter("@merchantId", merchantId)
                .WithParameter("@subscriptionKey", subscriptionKey);

            return await FirstOrDefaultAsync(query);
        }
        catch (Exception ex)
        {
            throw new CosmosGetException(ex.Message);
        }
    }

    public async Task<ApiUserConfigResponse?> GetApiUserConfigAsync(string clientId, string merchantId)
    {
        try
        {
            var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.clientId = @clientId AND c.merchantId = @merchantId")
                .WithParameter("@clientId", clientId)
                .WithParameter("@merchantId", merchantId);

            return await FirstOrDefaultAsync(query);
        }
        catch (Exception ex)
        {
            throw new CosmosGetException(ex.Message);
        }
    }

    private async Task<ApiUserConfigResponse?> FirstOrDefaultAsync(QueryDefinition query)
    {
        using var iterator = _container.GetItemQueryIterator<ApiUserConfigResponse>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            var first = page.FirstOrDefault();
            if (first is not null)
                return first;
        }
        return null;
    }
}
