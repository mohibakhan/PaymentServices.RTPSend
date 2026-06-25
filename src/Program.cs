using Azure.Identity;
using FluentValidation;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
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
using Polly;
using Serilog;
using Serilog.Events;
using System.Diagnostics.CodeAnalysis;
using System.Net;

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

                // TabaPay client - Resilience handler
                // retries transient 429/503 with capped, Retry-After-aware backoff
                // so a rate-limited sandbox doesn't immediately fail the payment.
                // If TabaPay asks for longer than the cap (e.g. "retry in 31s"),
                // we put the message in dlq and let Service Bus redeliver later rather than
                // hold the message lock.
                services.AddHttpClient("TabaPaySendService")
                    .AddResilienceHandler("tabapay-retry", (builder, context) =>
                    {
                        // Same logger factory as the rest of the app, so the
                        // EvolveId/ReferenceId scope set by TabaPaySendService is
                        // picked up on these retry lines too.
                        var retryLogger = context.ServiceProvider
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("TabaPay.Resilience");

                        builder.AddRetry(new HttpRetryStrategyOptions
                        {
                            ShouldHandle = args => ValueTask.FromResult(
                                args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests }
                                                     or { StatusCode: HttpStatusCode.ServiceUnavailable }
                                || HttpClientResiliencePredicates.IsTransient(args.Outcome)),
                            MaxRetryAttempts = 3,
                            BackoffType = DelayBackoffType.Exponential,
                            UseJitter = true,
                            Delay = TimeSpan.FromSeconds(1),
                            OnRetry = args =>
                            {
                                // Makes the otherwise-silent in-call retries visible:
                                // which attempt, how long the wait, and why.
                                retryLogger.LogWarning(
                                    "TabaPay transient response; in-call retry {RetryNumber}/3 in {DelayMs}ms. " +
                                    "HttpStatus={Status} Error={Error}",
                                    args.AttemptNumber + 1,
                                    (int)args.RetryDelay.TotalMilliseconds,
                                    args.Outcome.Result?.StatusCode,
                                    args.Outcome.Exception?.Message);
                                return default;
                            },
                            DelayGenerator = args =>
                            {
                                var capped = TimeSpan.FromSeconds(6);

                                // Respect Retry-After when present, clamped to the cap.
                                var retryAfter = args.Outcome.Result?.Headers?.RetryAfter;
                                if (retryAfter is not null)
                                {
                                    TimeSpan? hinted =
                                        retryAfter.Delta
                                        ?? (retryAfter.Date is { } d ? d - DateTimeOffset.UtcNow : null);

                                    if (hinted is { } h && h > TimeSpan.Zero)
                                    {
                                        if (h > capped)
                                            return ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero);
                                        return ValueTask.FromResult<TimeSpan?>(h);
                                    }
                                }

                                var backoff = args.AttemptNumber switch
                                {
                                    0 => TimeSpan.FromSeconds(1),
                                    1 => TimeSpan.FromSeconds(2),
                                    _ => TimeSpan.FromSeconds(4),
                                };
                                return ValueTask.FromResult<TimeSpan?>(backoff < capped ? backoff : capped);
                            }
                        });

                        builder.AddTimeout(TimeSpan.FromSeconds(20));
                    });

                // Gateway client — HTTP call to /tptch/send
                services.AddHttpClient<IGatewayClient, GatewayClient>();

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
}