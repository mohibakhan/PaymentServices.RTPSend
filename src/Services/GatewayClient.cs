using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Settings;

namespace PaymentServices.RTPSend.Services;

public interface IGatewayClient
{
    /// <summary>
    /// POSTs the payment to the Gateway tptch/send endpoint. Gateway validates,
    /// dedupes, persists and publishes to the async pipeline, returning 202.
    /// Throws <see cref="GatewayCallException"/> on a non-success response so the
    /// orchestrator (and SB retry) can handle it.
    /// </summary>
    Task SendAsync(EvolvePaymentRequest payment, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request body for Gateway POST /tptch/send. Mirrors Gateway's TchSendRequest
/// contract (camelCase on the wire).
/// </summary>
public sealed class GatewayTchSendRequest
{
    public string EvolveId { get; set; } = string.Empty;
    public string FintechId { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string FboAccount { get; set; } = string.Empty;
    public string? RemittanceInformation { get; set; }
    public bool UserIsBusiness { get; set; }
    public GatewayAccount SourceAccount { get; set; } = new();
    public GatewayAccount DestinationAccount { get; set; } = new();
}

public sealed class GatewayAccount
{
    public string AccountNumber { get; set; } = string.Empty;
    public string RoutingNumber { get; set; } = string.Empty;
    public GatewayName Name { get; set; } = new();
}

public sealed class GatewayName
{
    public string? First { get; set; }
    public string? Last { get; set; }
    public string? Company { get; set; }
}

public sealed class GatewayCallException : Exception
{
    public int? StatusCode { get; }
    public GatewayCallException(string message, int? statusCode = null) : base(message)
        => StatusCode = statusCode;
}

public sealed class GatewayClient : IGatewayClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;
    private readonly RtpSendSettings _settings;
    private readonly ILogger<GatewayClient> _logger;

    public GatewayClient(
        HttpClient httpClient,
        IOptions<RtpSendSettings> settings,
        ILogger<GatewayClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(EvolvePaymentRequest payment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.GATEWAY_TPTCH_SEND_URL))
            throw new GatewayCallException("GATEWAY_TPTCH_SEND_URL is not configured.");

        var body = MapToRequest(payment);

        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.GATEWAY_TPTCH_SEND_URL)
        {
            Content = JsonContent.Create(body, options: _jsonOptions)
        };

        if (!string.IsNullOrWhiteSpace(_settings.GATEWAY_TPTCH_SEND_APIKEY))
            request.Headers.Add("x-functions-key", _settings.GATEWAY_TPTCH_SEND_APIKEY);

        _logger.LogInformation(
            "Calling Gateway tptch/send. EvolveId={EvolveId}", payment.EvolveId);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway tptch/send call failed (transport). EvolveId={EvolveId}", payment.EvolveId);
            throw new GatewayCallException($"Gateway call failed: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Gateway tptch/send returned {StatusCode}. EvolveId={EvolveId} Body={Body}",
                (int)response.StatusCode, payment.EvolveId, responseBody);
            throw new GatewayCallException(
                $"Gateway returned {(int)response.StatusCode}", (int)response.StatusCode);
        }

        _logger.LogInformation(
            "Gateway tptch/send accepted. EvolveId={EvolveId} StatusCode={StatusCode}",
            payment.EvolveId, (int)response.StatusCode);
    }

    private static GatewayTchSendRequest MapToRequest(EvolvePaymentRequest p) => new()
    {
        EvolveId = p.EvolveId,
        FintechId = p.FintechId ?? string.Empty,
        Amount = p.Amount ?? string.Empty,
        TaxId = p.TaxId ?? string.Empty,
        FboAccount = p.FboAccountNumber ?? string.Empty,
        RemittanceInformation = p.RemittanceInformation,
        UserIsBusiness = p.UserIsBusiness,
        SourceAccount = new GatewayAccount
        {
            AccountNumber = p.SourceAccount?.AccountNumber ?? string.Empty,
            RoutingNumber = p.SourceAccount?.RoutingNumber ?? string.Empty,
            Name = new GatewayName
            {
                First = p.SourceAccount?.Name?.First,
                Last = p.SourceAccount?.Name?.Last,
                Company = p.SourceAccount?.Name?.Company
            }
        },
        DestinationAccount = new GatewayAccount
        {
            AccountNumber = p.DestinationAccount?.AccountNumber ?? string.Empty,
            RoutingNumber = p.DestinationAccount?.RoutingNumber ?? string.Empty,
            Name = new GatewayName
            {
                First = p.DestinationAccount?.Name?.First,
                Last = p.DestinationAccount?.Name?.Last,
                Company = p.DestinationAccount?.Name?.Company
            }
        }
    };
}