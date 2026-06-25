namespace PaymentServices.RTPSend.Interface.External;

/// <summary>
/// PLACEHOLDER. Swap this interface (and the registration in
/// ServiceCollectionExtensions) for the real LedgerService NuGet package once
/// it ships. Until then, register <c>NoOpLedgerService</c> so DI resolves.
/// </summary>
public interface ILedgerService
{
    /// <summary>
    /// Reserves funds for the payment. Should be called AFTER limit checks pass and
    /// BEFORE TabaPay is invoked. The returned <c>ReservationId</c> can be used by
    /// downstream code for compensation if needed.
    /// </summary>
    Task<LedgerReservationResult> ReserveAsync(LedgerReservationRequest request, CancellationToken cancellationToken = default);
}

public sealed class LedgerReservationRequest
{
    public string EvolveId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string MerchantId { get; init; } = string.Empty;
    public string FintechId { get; init; } = string.Empty;
    public string FboAccountNumber { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public string PaymentReference { get; init; } = string.Empty;
}

public sealed class LedgerReservationResult
{
    public bool Success { get; init; }
    public string? ReservationId { get; init; }
    public string? Reason { get; init; }
    public static LedgerReservationResult Ok(string reservationId) =>
        new() { Success = true, ReservationId = reservationId };
    public static LedgerReservationResult Failed(string reason) =>
        new() { Success = false, Reason = reason };
}
