using Azure.Identity;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PaymentServices.RTPSend.Extensions;
using Serilog;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// -----------------------------------------------------------------------------
// Configuration — local.settings.json + Azure App Configuration (Managed Identity)
// -----------------------------------------------------------------------------
var appConfigEndpoint = builder.Configuration["APP_CONFIG_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(appConfigEndpoint))
{
    var miClientId = builder.Configuration["AZURE_CLIENT_ID"];
    var credential = string.IsNullOrWhiteSpace(miClientId)
        ? (Azure.Core.TokenCredential)new DefaultAzureCredential()
        : new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = miClientId
        });

    builder.Configuration.AddAzureAppConfiguration(options =>
    {
        options
            .Connect(new Uri(appConfigEndpoint), credential)
            .Select("rtpSend:*")
            .ConfigureKeyVault(kv => kv.SetCredential(credential));
    });
}

// -----------------------------------------------------------------------------
// Application Insights (App Insights worker service)
// -----------------------------------------------------------------------------
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// -----------------------------------------------------------------------------
// Serilog → Application Insights sink
// -----------------------------------------------------------------------------
builder.Services.AddSerilog((sp, logger) =>
{
    var telemetry = sp.GetRequiredService<TelemetryConfiguration>();
    logger
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.ApplicationInsights(telemetry, TelemetryConverter.Traces);
});

// -----------------------------------------------------------------------------
// RTPSend services
// -----------------------------------------------------------------------------
builder.Services.AddRtpSendInfrastructure(builder.Configuration);

await builder.Build().RunAsync();
