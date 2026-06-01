using Azure.Identity;
using Evolve.Digital.LedgerService.Shared.Internal;
using Evolve.Digital.LedgerService.Shared.Services;
using FluentValidation;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Providers;
using PaymentServices.RTPSend.Repositories;
using PaymentServices.RTPSend.Repositories.Adapters;
using PaymentServices.RTPSend.Services;
using PaymentServices.RTPSend.Settings;
using PaymentServices.RTPSend.Validators;
using PaymentServices.Shared.Extensions;
using Serilog;
using Serilog.Events;
using System.Diagnostics.CodeAnalysis;
using LedgerLibILedgerService = Evolve.Digital.LedgerService.Shared.Services.ILedgerService;
using LedgerLibLedgerService = Evolve.Digital.LedgerService.Shared.Services.LedgerService;
using RtpSendILedgerService = PaymentServices.RTPSend.Interface.External.ILedgerService;
using RtpSendILimitService = PaymentServices.RTPSend.Interface.External.ILimitService;

namespace PaymentServices.RTPSend;

[ExcludeFromCodeCoverage]
public static class Program
{
    private const string Prefix = "rtpSend:AppSettings";

    public static async Task Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(SetupAppConfiguration)
            .ConfigureFunctionsWebApplication()
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;

                SetupSerilog(config);

                // Application Insights
                services.AddApplicationInsightsTelemetryWorkerService();
                services.ConfigureFunctionsApplicationInsights();

                // Shared platform infrastructure (PaymentServices.Shared)
                services.AddPaymentAppSettings(config, Prefix);
                services.AddPaymentCosmosClient(config, Prefix);
                services.AddPaymentServiceBusPublisher(config, Prefix);

                // RTPSend-specific settings
                services.AddOptions<RtpSendSettings>()
                    .Configure<IConfiguration>((settings, cfg) =>
                        cfg.GetSection(Prefix).Bind(settings));

                // Cosmos containers
                RegisterCosmosContainers(services, config);

                // Adapters / repositories
                services.AddScoped<IPaymentCosmosDBAdapter, PaymentCosmosDBAdapter>();
                services.AddScoped<IPartnerLedgerCosmosDBAdapter, PartnerLedgerCosmosDBAdapter>();
                services.AddScoped<IApiUserConfigCosmosAdapter, ApiUserConfigAdapter>();
                services.AddScoped<IPaymentIdempotencyAdapter, PaymentIdempotencyAdapter>();
                services.AddSingleton<IServiceBusAdapter, ServiceBusAdapter>();

                // Helpers
                services.AddScoped<IEvolvePaymentRequestHelper, EvolvePaymentRequestHelper>();
                services.AddScoped<IProblemHelper, ProblemHelper>();

                // HTTP + HttpContext
                services.AddHttpClient();
                services.AddHttpContextAccessor();

                // External services
                //
                // LimitService is still a placeholder until that NuGet ships.
                services.AddScoped<RtpSendILimitService, NoOpLimitService>();

                // Real ledger service via Evolve.Digital.LedgerService.Shared(.Internal)
                RegisterLedgerServices(services);

                // Core business services
                services.AddScoped<IPartnerLedgerSystem, PartnerLedgerSystem>();
                services.AddScoped<ITabaPaySendService, TabaPaySendService>();
                services.AddScoped<IServiceBusMessageService, ServiceBusMessageService>();
                services.AddScoped<IPaymentOrchestrator, PaymentOrchestrator>();

                // Validation
                services.AddValidatorsFromAssemblyContaining<BasicPaymentRequestValidator>();

                // Health checks
                services.AddHealthChecks();
                services.AddSingleton<IHealthCheckServiceProvider, HealthCheckServiceProvider>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.Services.Configure<LoggerFilterOptions>(options =>
                {
                    var defaultRule = options.Rules.FirstOrDefault(rule =>
                        rule.ProviderName ==
                        "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");

                    if (defaultRule is not null)
                        options.Rules.Remove(defaultRule);
                });

                logging.AddSerilog(dispose: true);
            })
            .Build();

        await host.RunAsync();
    }

    private static void SetupAppConfiguration(IConfigurationBuilder builder)
    {
        builder.AddEnvironmentVariables();
        var settings = builder.Build();

        var appConfigUrl = settings["AppConfig:Endpoint"];
        var azureClientId = settings["AZURE_CLIENT_ID"];

        if (!string.IsNullOrWhiteSpace(appConfigUrl) && !string.IsNullOrWhiteSpace(azureClientId))
        {
            var credentialOptions = new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = azureClientId
            };
            var credential = new DefaultAzureCredential(credentialOptions);

            builder.AddAzureAppConfiguration(options =>
            {
                options
                    .Connect(new Uri(appConfigUrl), credential)
                    .Select("rtpSend:*")
                    .Select("telemetry:*")
                    .ConfigureKeyVault(kv => kv.SetCredential(credential));
            });
        }

        builder
            .SetBasePath(Environment.CurrentDirectory)
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: false);
    }

    private static void SetupSerilog(IConfiguration config)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Azure.Functions.Worker", LogEventLevel.Warning)
            .MinimumLevel.Override("Host", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "PaymentServices.RTPSend")
            .Enrich.WithProperty("Environment",
                Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Production")
            .CreateLogger();
    }

    private static void RegisterCosmosContainers(IServiceCollection services, IConfiguration config)
    {
        var database = config[$"{Prefix}:COSMOS_DATABASE"]
            ?? throw new InvalidOperationException($"{Prefix}:COSMOS_DATABASE is required");

        services.AddKeyedSingleton<Container>("payments", (sp, _) =>
        {
            var client = sp.GetRequiredService<CosmosClient>();
            var container = config[$"{Prefix}:COSMOS_PAYMENT_CONTAINER"] ?? "paymentRequests";
            return client.GetContainer(database, container);
        });

        services.AddKeyedSingleton<Container>("partnerLedger", (sp, _) =>
        {
            var client = sp.GetRequiredService<CosmosClient>();
            var container = config[$"{Prefix}:COSMOS_PARTNER_LEDGER_CONTAINER"] ?? "partnerLedger";
            return client.GetContainer(database, container);
        });

        services.AddKeyedSingleton<Container>("apiUserConfig", (sp, _) =>
        {
            var client = sp.GetRequiredService<CosmosClient>();
            var container = config[$"{Prefix}:COSMOS_API_CONFIG_CONTAINER"] ?? "apiUserConfig";
            return client.GetContainer(database, container);
        });

        services.AddKeyedSingleton<Container>("paymentIdempotency", (sp, _) =>
        {
            var client = sp.GetRequiredService<CosmosClient>();
            var container = config[$"{Prefix}:COSMOS_IDEMPOTENCY_CONTAINER"] ?? "paymentIdempotency";
            return client.GetContainer(database, container);
        });
    }

    /// <summary>
    /// Registers the upstream Evolve.Digital.LedgerService.Shared(.Internal)
    /// services and wires RTPSend's ILedgerService to the adapter
    /// (EvolveLedgerService) that delegates to ILedgerInternalClient.
    ///
    /// Ledger data lives in a SEPARATE Cosmos database from RTPSend's data,
    /// but in the SAME Cosmos account — so we reuse the registered CosmosClient.
    /// </summary>
    private static void RegisterLedgerServices(IServiceCollection services)
    {
        // Upstream library's ILedgerService (concrete LedgerService — needs
        // CosmosClient + database name)
        services.AddSingleton<LedgerLibILedgerService>(sp =>
        {
            var cosmos = sp.GetRequiredService<CosmosClient>();
            var settings = sp.GetRequiredService<IOptions<RtpSendSettings>>().Value;
            return new LedgerLibLedgerService(cosmos, settings.LEDGER_COSMOS_DATABASE);
        });

        // Upstream library's IBatchService — same constructor shape as
        // LedgerService. If BatchService takes a different signature, swap
        // this line for whatever its real constructor expects.
        services.AddSingleton<IBatchService>(sp =>
        {
            var cosmos = sp.GetRequiredService<CosmosClient>();
            var settings = sp.GetRequiredService<IOptions<RtpSendSettings>>().Value;
            return new BatchService(cosmos, settings.LEDGER_COSMOS_DATABASE);
        });

        // Upstream library's internal client (depends on ILedgerService + IBatchService)
        services.AddSingleton<ILedgerInternalClient, LedgerInternalClient>();

        // RTPSend's ILedgerService — now the adapter, not a NoOp
        services.AddScoped<RtpSendILedgerService, EvolveLedgerService>();
    }
}
