using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PaymentServices.RTPSend.Providers;

public interface IHealthCheckServiceProvider
{
    Task<HealthReport> GetHealthAsync();
}

[ExcludeFromCodeCoverage]
public sealed class HealthCheckServiceProvider : IHealthCheckServiceProvider
{
    private readonly HealthCheckService _healthCheckService;

    public HealthCheckServiceProvider(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    public Task<HealthReport> GetHealthAsync() => _healthCheckService.CheckHealthAsync();
}
