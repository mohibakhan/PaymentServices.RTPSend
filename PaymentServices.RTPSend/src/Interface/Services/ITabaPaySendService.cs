using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Response;

namespace PaymentServices.RTPSend.Interface.Services;

/// <summary>
/// Single-responsibility TabaPay caller. Sends, patches Cosmos with the result,
/// throws <see cref="Exceptions.TabaPayProcessingException"/> on any failure.
/// Service-Bus publishing and downstream notifications are NOT handled here.
/// </summary>
public interface ITabaPaySendService
{
    Task<TabaPaySendResult> ProcessPayment(EvolvePaymentRequest cosmosPaymentItem);
}

public sealed class TabaPaySendResult
{
    public required EvolvePaymentRequest Document { get; init; }
    public required TabaPayResponse Response { get; init; }
    public required string RawResponse { get; init; }
}
