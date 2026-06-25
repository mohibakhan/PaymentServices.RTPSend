namespace PaymentServices.RTPSend.Interface.External;

/// <summary>
/// PLACEHOLDER. Swap this interface (and the registration in
/// ServiceCollectionExtensions) for the real LimitService NuGet package once
/// it ships. Until then, register <c>NoOpLimitService</c> so DI resolves.
/// </summary>
public interface ILimitService
{
    /// <summary>
    /// Validates that the requested payment is within the customer's configured limits
    /// (per-transaction, daily, monthly, etc).
    /// </summary>
    /// <returns>A result describing whether the payment is allowed.</returns>
    Task<LimitCheckResult> CheckAsync(LimitCheckRequest request, CancellationToken cancellationToken = default);
}

public sealed class LimitCheckRequest
{
    public string EvolveId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string MerchantId { get; init; } = string.Empty;
    public string FintechId { get; init; } = string.Empty;
    public string FboAccountNumber { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
}

public sealed class LimitCheckResult
{
    public bool Allowed { get; init; }
    public string? Reason { get; init; }
    public static LimitCheckResult Ok() => new() { Allowed = true };
    public static LimitCheckResult Denied(string reason) => new() { Allowed = false, Reason = reason };
}

/// <summary>
/// No-op implementation that always allows. Wire this up in DI so the project
/// builds and runs end-to-end before the real NuGet package is available.
/// Replace with the real implementation when ready.
/// </summary>
public sealed class NoOpLimitService : ILimitService
{
    public Task<LimitCheckResult> CheckAsync(LimitCheckRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(LimitCheckResult.Ok());
}
