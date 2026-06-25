using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Models.Cosmos;

namespace PaymentServices.RTPSend.Repositories;

/// <summary>
/// Cosmos adapter for the rtpSend payment-requests container.
/// Receives a keyed <see cref="Container"/> wired up by
/// <c>services.AddCosmosContainer(config, "&lt;paymentRequests&gt;", "payments", "rtpSend:AppSettings")</c>
/// in Program.cs.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class PaymentCosmosDBAdapter : IPaymentCosmosDBAdapter
{
    private readonly Container _container;
    private readonly ILogger<PaymentCosmosDBAdapter> _logger;

    public PaymentCosmosDBAdapter(
        [FromKeyedServices("payments")] Container container,
        ILogger<PaymentCosmosDBAdapter> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<EvolvePaymentRequest?> GetItemAsync(string id, string evolvePaymentId)
    {
        try
        {
            var response = await _container.ReadItemAsync<EvolvePaymentRequest>(
                id, new PartitionKey(evolvePaymentId));
            return response.Resource;
        }
        catch (CosmosException ce)
        {
            _logger.LogError(
                "Error reading from Cosmos: status code {StatusCode}, message {Message}",
                ce.StatusCode, ce.Message);
            return null;
        }
    }

    public async Task<EvolvePaymentRequest?> CreateItemAsync(EvolvePaymentRequest evolvePaymentRequest)
    {
        try
        {
            var response = await _container.CreateItemAsync(
                evolvePaymentRequest,
                new PartitionKey(evolvePaymentRequest.EvolveId));
            return response.Resource;
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    public async Task<EvolvePaymentRequest?> UpdateItemAsync(EvolvePaymentRequest evolvePaymentRequest)
    {
        try
        {
            var response = await _container.UpsertItemAsync(
                evolvePaymentRequest,
                new PartitionKey(evolvePaymentRequest.EvolveId));
            return response.Resource;
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    public async Task<EvolvePaymentRequest?> PatchItemAsync(
        EvolvePaymentRequest evolvePaymentRequest,
        List<PatchOperation> patchOperations)
    {
        try
        {
            var response = await _container.PatchItemAsync<EvolvePaymentRequest>(
                evolvePaymentRequest.Id,
                new PartitionKey(evolvePaymentRequest.EvolveId),
                patchOperations);
            return response.Resource;
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    public Task<List<EvolvePaymentRequest>> FindAllItemsAsync(string evolveId)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.evolveId = @evolveId")
            .WithParameter("@evolveId", evolveId);
        return RunQueryAsync(query);
    }

    public Task<List<EvolvePaymentRequest>> FindAllItemsAsync(string evolveId, string documentType, string documentSubType)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.evolveId = @evolveId AND c.documentType = @documentType AND c.documentSubType = @documentSubType")
            .WithParameter("@evolveId", evolveId)
            .WithParameter("@documentType", documentType)
            .WithParameter("@documentSubType", documentSubType);
        return RunQueryAsync(query);
    }

    public Task<List<EvolvePaymentRequest>> GetPayment(string evolveId, IHeaderDictionary headers)
    {
        var clientId = headers["x-client-id"].ToString();
        var merchantId = headers["x-merchant-id"].ToString();

        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.evolveId = @evolveId AND c.clientId = @clientId AND c.merchantId = @merchantId")
            .WithParameter("@evolveId", evolveId)
            .WithParameter("@clientId", clientId)
            .WithParameter("@merchantId", merchantId);

        return RunQueryAsync(query);
    }

    public Task<List<EvolvePaymentRequest>> GetPaymentByReference(string paymentReference, IHeaderDictionary headers)
    {
        var clientId = headers["x-client-id"].ToString();
        var merchantId = headers["x-merchant-id"].ToString();

        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.paymentReference = @paymentReference AND c.clientId = @clientId AND c.merchantId = @merchantId")
            .WithParameter("@paymentReference", paymentReference)
            .WithParameter("@clientId", clientId)
            .WithParameter("@merchantId", merchantId);

        return RunQueryAsync(query);
    }

    private async Task<List<EvolvePaymentRequest>> RunQueryAsync(QueryDefinition query)
    {
        var results = new List<EvolvePaymentRequest>();
        using var iterator = _container.GetItemQueryIterator<EvolvePaymentRequest>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            results.AddRange(page);
        }
        return results;
    }
}
