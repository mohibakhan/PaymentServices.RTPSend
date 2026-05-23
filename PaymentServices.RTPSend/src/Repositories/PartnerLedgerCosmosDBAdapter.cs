using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Models.Request;
using PaymentServices.RTPSend.Models.Response;
using PaymentServices.Shared.Infrastructure;
using PaymentServices.Shared.Interfaces;

namespace PaymentServices.RTPSend.Repositories;

[ExcludeFromCodeCoverage]
public sealed class PartnerLedgerCosmosDBAdapter : IPartnerLedgerCosmosDBAdapter
{
    private readonly Container _container;
    private readonly ICosmosRepository<PartnerLedgerRequest> _repository;
    private readonly ILogger<PartnerLedgerCosmosDBAdapter> _logger;

    public PartnerLedgerCosmosDBAdapter(
        [FromKeyedServices("partnerLedger")] Container container,
        ILogger<PartnerLedgerCosmosDBAdapter> logger)
    {
        _container = container;
        _repository = new CosmosRepository<PartnerLedgerRequest>(container);
        _logger = logger;
    }

    public async Task<PartnerLedgerResponse> GetItemAsync(string accountNumber)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.vAccountNumber = @accountNumber")
                .WithParameter("@accountNumber", accountNumber);

            var results = new List<PartnerLedgerResponse>();
            using var iterator = _container.GetItemQueryIterator<PartnerLedgerResponse>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync();
                results.AddRange(page);
            }

            var response = results.FirstOrDefault();
            return response ?? new PartnerLedgerResponse { ErrorMessage = "Invalid V Account " };
        }
        catch (Exception ex)
        {
            throw new CosmosGetException(ex.Message);
        }
    }

    public async Task<PartnerLedgerRequest?> CreateItemAsync(PartnerLedgerResponse partnerLedgerResponse)
    {
        var request = new PartnerLedgerRequest
        {
            Id = Guid.NewGuid().ToString(),
            CifNo = partnerLedgerResponse.CifNo,
            FboAccount = partnerLedgerResponse.FboAccount,
            FboAccountName = partnerLedgerResponse.FboAccountName,
            TaxId = partnerLedgerResponse.TaxId,
            UserIsBusiness = partnerLedgerResponse.UserIsBusiness,
            VAccountNumber = partnerLedgerResponse.VAccountNumber,
            AccountStatus = partnerLedgerResponse.AccountStatus
        };

        try
        {
            return await _repository.CreateAsync(request, request.Id);
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return null;
        }
    }
}
