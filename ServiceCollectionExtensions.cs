using Evolve.Digital.LedgerService.Shared.Internal;
using Evolve.Digital.LedgerService.Shared.Services;
using FluentValidation;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
using LedgerLibILedgerService = Evolve.Digital.LedgerService.Shared.Services.ILedgerService;
using LedgerLibLedgerService = Evolve.Digital.LedgerService.Shared.Services.LedgerService;
using RtpSendILedgerService = PaymentServices.RTPSend.Interface.External.ILedgerService;
using RtpSendILimitService = PaymentServices.RTPSend.Interface.External.ILimitService;

namespace PaymentServices.RTPSend.Extensions;

/// <summary>
/// Registers everything the RTPSend Function App needs.
/// Layout mirrors PaymentServices.Gateway.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string Prefix = "rtpSend:AppSettings";

    public static IServiceCollection AddRtpSendInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ---------------------------------------------------------------
        // Shared platform infra (PaymentServices.Shared)
        // ---------------------------------------------------------------
        services.AddPaymentAppSettings(configuration, Prefix);
        services.AddPaymentCosmosClient(configuration, Prefix);
        services.AddPaymentServiceBusPublisher(configuration, Prefix);

        // ---------------------------------------------------------------
        // RTPSend-specific settings
        // ---------------------------------------------------------------
        services.Configure<RtpSendSettings>(configuration.GetSection(Prefix));

        // ---------------------------------------------------------------
        // Cosmos containers — keyed per-container
        // ---------------------------------------------------------------
        var rtpSendSection = configuration.GetSection(Prefix);
        var paymentsContainer = rtpSendSection["COSMOS_PAYMENT_CONTAINER"]
            ?? throw new InvalidOperationException($"{Prefix}:COSMOS_PAYMENT_CONTAINER is required");
        var partnerLedgerContainer = rtpSendSection["COSMOS_PARTNER_LEDGER_CONTAINER"]
            ?? throw new InvalidOperationException($"{Prefix}:COSMOS_PARTNER_LEDGER_CONTAINER is required");
        var apiUserConfigContainer = rtpSendSection["COSMOS_API_CONFIG_CONTAINER"]
            ?? throw new InvalidOperationException($"{Prefix}:COSMOS_API_CONFIG_CONTAINER is required");
        var idempotencyContainer = rtpSendSection["COSMOS_IDEMPOTENCY_CONTAINER"]
            ?? throw new InvalidOperationException($"{Prefix}:COSMOS_IDEMPOTENCY_CONTAINER is required");

        services.AddCosmosContainer(configuration, paymentsContainer,      serviceKey: "payments",            prefix: Prefix);
        services.AddCosmosContainer(configuration, partnerLedgerContainer, serviceKey: "partnerLedger",       prefix: Prefix);
        services.AddCosmosContainer(configuration, apiUserConfigContainer, serviceKey: "apiUserConfig",       prefix: Prefix);
        services.AddCosmosContainer(configuration, idempotencyContainer,   serviceKey: "paymentIdempotency",  prefix: Prefix);

        // ---------------------------------------------------------------
        // Adapters / repositories
        // ---------------------------------------------------------------
        services.AddScoped<IPaymentCosmosDBAdapter, PaymentCosmosDBAdapter>();
        services.AddScoped<IPartnerLedgerCosmosDBAdapter, PartnerLedgerCosmosDBAdapter>();
        services.AddScoped<IApiUserConfigCosmosAdapter, ApiUserConfigAdapter>();
        services.AddScoped<IPaymentIdempotencyAdapter, PaymentIdempotencyAdapter>();
        services.AddSingleton<IServiceBusAdapter, ServiceBusAdapter>();

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        services.AddScoped<IEvolvePaymentRequestHelper, EvolvePaymentRequestHelper>();
        services.AddScoped<IProblemHelper, ProblemHelper>();

        // ---------------------------------------------------------------
        // HTTP + HttpContext
        // ---------------------------------------------------------------
        services.AddHttpClient();
        services.AddHttpContextAccessor();

        // ---------------------------------------------------------------
        // External services
        //
        // LimitService is still a placeholder until that NuGet ships.
        // LedgerService is now backed by the Evolve.Digital.LedgerService
        // NuGet packages via the EvolveLedgerService adapter — see
        // RegisterLedgerServices below.
        // ---------------------------------------------------------------
        services.AddScoped<RtpSendILimitService, NoOpLimitService>();
        RegisterLedgerServices(services);

        // ---------------------------------------------------------------
        // Core business services
        // ---------------------------------------------------------------
        services.AddScoped<IPartnerLedgerSystem, PartnerLedgerSystem>();
        services.AddScoped<ITabaPaySendService, TabaPaySendService>();
        services.AddScoped<IServiceBusMessageService, ServiceBusMessageService>();
        services.AddScoped<IPaymentOrchestrator, PaymentOrchestrator>();

        // ---------------------------------------------------------------
        // Validation
        // ---------------------------------------------------------------
        services.AddValidatorsFromAssemblyContaining<BasicPaymentRequestValidator>();

        // ---------------------------------------------------------------
        // Health checks
        // ---------------------------------------------------------------
        services.AddHealthChecks();
        services.AddSingleton<IHealthCheckServiceProvider, HealthCheckServiceProvider>();

        return services;
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

        // RTPSend's ILedgerService — the adapter, scoped (matches orchestrator lifetime)
        services.AddScoped<RtpSendILedgerService, EvolveLedgerService>();
    }
}
