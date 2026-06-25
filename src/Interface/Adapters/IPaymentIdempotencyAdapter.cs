using PaymentServices.RTPSend.Models.Cosmos;

namespace PaymentServices.RTPSend.Interface.Adapters;

/// <summary>
/// Adapter for the dedicated <c>paymentIdempotency</c> container.
/// Used by CreatePayment to atomically reserve a paymentReference and detect
/// duplicate requests. The container should be partitioned on
/// <c>/paymentReference</c> and have TTL enabled at the container level
/// (DefaultTimeToLive >= 0).
/// </summary>
public interface IPaymentIdempotencyAdapter
{
    /// <summary>
    /// Attempts to atomically insert an idempotency entry. Returns <c>true</c>
    /// if the insert succeeded (this is a fresh paymentReference) and
    /// <c>false</c> if a duplicate already exists (Cosmos returned 409).
    /// Any other Cosmos error is propagated to the caller.
    /// </summary>
    Task<bool> TryReserveAsync(PaymentIdempotencyEntry entry);
}
