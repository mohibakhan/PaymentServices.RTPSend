using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Providers;

namespace PaymentServices.RTPSend.Functions;

public class HealthCheck
{
    private readonly IHealthCheckServiceProvider _healthCheckServiceProvider;
    private readonly ILogger<HealthCheck> _log;

    public HealthCheck(IHealthCheckServiceProvider healthCheckServiceProvider, ILogger<HealthCheck> logger)
    {
        _healthCheckServiceProvider = healthCheckServiceProvider;
        _log = logger;
    }

    [Function("Health")]
    public async Task<IActionResult> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
    {
        _log.LogInformation("Received health check request");
        var report = await _healthCheckServiceProvider.GetHealthAsync();
        return new OkObjectResult(report.Status.ToString());
    }
}
