using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Models.Cosmos;

namespace PaymentServices.RTPSend.Repositories;

/// <summary>
/// Atomic-dedupe adapter for the <c>paymentIdempotency</c> container.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class PaymentIdempotencyAdapter : IPaymentIdempotencyAdapter
{
    private readonly Container _container;
    private readonly ILogger<PaymentIdempotencyAdapter> _logger;

    public PaymentIdempotencyAdapter(
        [FromKeyedServices("paymentIdempotency")] Container container,
        ILogger<PaymentIdempotencyAdapter> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<bool> TryReserveAsync(PaymentIdempotencyEntry entry)
    {
        try
        {
            await _container.CreateItemAsync(
                entry,
                new PartitionKey(entry.PaymentReference));
            return true;
        }
        catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation(
                "Duplicate paymentReference detected via idempotency container: {Ref}",
                entry.PaymentReference);
            return false;
        }
    }
}
