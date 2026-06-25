using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Models.Request;
using PaymentServices.RTPSend.Models.Response;
using PaymentServices.RTPSend.Settings;

namespace PaymentServices.RTPSend.Services;

/// <summary>
/// Single responsibility: POST to TabaPay, patch the Cosmos document with the
/// outcome, return a <see cref="TabaPaySendResult"/>.
/// </summary>
public sealed class TabaPaySendService : ITabaPaySendService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<TabaPaySendService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RtpSendSettings _settings;
    private readonly IPaymentCosmosDBAdapter _paymentCosmosDB;

    public TabaPaySendService(
        ILogger<TabaPaySendService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<RtpSendSettings> settings,
        IPaymentCosmosDBAdapter paymentCosmosDB)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _paymentCosmosDB = paymentCosmosDB;
    }

    public async Task<TabaPaySendResult> ProcessPayment(EvolvePaymentRequest cosmosPaymentItem)
    {
        var tabapayRequest = TabaPayRequestHelper.ConvertEvolveToTabaPayRequest(
            cosmosPaymentItem,
            _settings.TABAPAY_SOURCE_ACCOUNT_ID);

        // Tag every line emitted during this call — including the resilience
        // handler's retry logs — with the correlation keys, so a single payment
        // is traceable end-to-end even when many run concurrently.
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["EvolveId"] = cosmosPaymentItem.EvolveId,
            ["ReferenceId"] = tabapayRequest.ReferenceId
        });

        _logger.LogInformation("TabaPay processing for evolveId {EvolveId}", cosmosPaymentItem.EvolveId);

        HttpResponseMessage httpResponse;
        string rawBody;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            httpResponse = await SendTransactionAsync(tabapayRequest);
            rawBody = await httpResponse.Content.ReadAsStringAsync();
            stopwatch.Stop();

            // Full body at Debug only — it can carry account numbers / PII.
            _logger.LogDebug("TabaPay raw response ({Status}) in {ElapsedMs}ms: {Body}",
                httpResponse.StatusCode, stopwatch.ElapsedMilliseconds, rawBody);
        }
        catch (HttpRequestException ex)
        {
            // Transport-level failure (DNS, connection reset, etc.) — transient, worth retrying.
            stopwatch.Stop();
            _logger.LogError(ex, "TabaPay HTTP call failed after {ElapsedMs}ms: {Message}",
                stopwatch.ElapsedMilliseconds, ex.Message);
            await PatchTransactionStatusAsync(
                RequestStage.TABAPAY, RequestStatus.FAILED, $"HTTP error: {ex.Message}", cosmosPaymentItem);
            throw new TabaPayProcessingException($"TabaPay HTTP call failed: {ex.Message}", ex, isRetryable: true);
        }
        catch (TaskCanceledException ex)
        {
            // Timeout — transient, worth retrying.
            stopwatch.Stop();
            _logger.LogError(ex, "TabaPay HTTP call timed out after {ElapsedMs}ms.", stopwatch.ElapsedMilliseconds);
            await PatchTransactionStatusAsync(
                RequestStage.TABAPAY, RequestStatus.FAILED, "Timeout calling TabaPay", cosmosPaymentItem);
            throw new TabaPayProcessingException("TabaPay HTTP call timed out", ex, isRetryable: true);
        }

        var tabaPayResponse = TryDeserialize(rawBody);
        var isSuccess =
            (int)httpResponse.StatusCode == (int)HttpStatusCode.OK
            && tabaPayResponse?.Status == PaymentRequestConstants.TabaPayComplete
            && tabaPayResponse.Sc == (int)HttpStatusCode.OK;

        // Transient (retry): 5xx server faults, plus 429 (rate limited) and 408
        // (request timeout) that survived the in-call resilience retries — those
        // clear on their own given time. Deterministic failures (other 4xx like a
        // bad softDescriptor, or an HTTP-200 business decline) fail identically on
        // every retry, so they're terminal and get a distinct status.
        var statusCode = (int)httpResponse.StatusCode;
        var isRetryable = !isSuccess &&
            (statusCode >= 500
             || statusCode == (int)HttpStatusCode.TooManyRequests   // 429
             || statusCode == (int)HttpStatusCode.RequestTimeout);  // 408

        await PatchTransactionStatusAsync(
            RequestStage.TABAPAY,
            isSuccess ? RequestStatus.COMPLETED
                      : isRetryable ? RequestStatus.FAILED
                                    : RequestStatus.FAILED_TABAPAY,
            rawBody,
            cosmosPaymentItem);

        if (!isSuccess)
        {
            // Structured decline fields (no PII); the full body is at Debug above.
            // Terminal declines log at Error so they alert separately from transient blips.
            _logger.Log(
                isRetryable ? LogLevel.Warning : LogLevel.Error,
                "TabaPay non-success ({Retryability}) in {ElapsedMs}ms. " +
                "HttpStatus={Status} SC={Sc} EC={Ec} NetworkRC={NetworkRc} TabaStatus={TabaStatus}",
                isRetryable ? "retryable" : "terminal",
                stopwatch.ElapsedMilliseconds,
                httpResponse.StatusCode,
                tabaPayResponse?.Sc,
                tabaPayResponse?.Ec,
                tabaPayResponse?.NetworkRc,
                tabaPayResponse?.Status);
            throw new TabaPayProcessingException(
                $"TabaPay returned non-success. HTTP status: {httpResponse.StatusCode}. Response: {rawBody}",
                isRetryable: isRetryable,
                statusCode: httpResponse.StatusCode);
        }

        // Patch transaction IDs onto the Cosmos document
        var patched = await PatchTabaPayIdsAsync(tabaPayResponse!, tabapayRequest.ReferenceId, cosmosPaymentItem);

        // Reconciliation keys for matching this payment against TabaPay's ledger.
        _logger.LogInformation(
            "TabaPay COMPLETED in {ElapsedMs}ms. TransactionId={TransactionId} Network={Network} " +
            "NetworkId={NetworkId} ApprovalCode={ApprovalCode}",
            stopwatch.ElapsedMilliseconds,
            tabaPayResponse!.TransactionId,
            tabaPayResponse.Network,
            tabaPayResponse.NetworkId,
            tabaPayResponse.ApprovalCode);

        return new TabaPaySendResult
        {
            Document = patched ?? cosmosPaymentItem,
            Response = tabaPayResponse!,
            RawResponse = rawBody
        };
    }

    private async Task<HttpResponseMessage> SendTransactionAsync(TabapayPaymentRequest tabaPayRequest)
    {
        var client = _httpClientFactory.CreateClient(nameof(TabaPaySendService));

        var json = JsonSerializer.Serialize(tabaPayRequest, _jsonOptions);
        // Full request body at Debug only — contains account numbers / PII.
        _logger.LogDebug("TabaPay request body: {Body}", json);

        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.TABAPAY_SEND_URL)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("x-Client-Id", _settings.TABAPAY_SEND_CLIENT_ID);
        request.Headers.TryAddWithoutValidation("x-merchant-id", _settings.TABAPAY_SEND_MERCHANT_ID);
        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _settings.TABAPAY_SEND_APIKEY);

        return await client.SendAsync(request);
    }

    private static TabaPayResponse? TryDeserialize(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonSerializer.Deserialize<TabaPayResponse>(body, _jsonOptions); }
        catch (JsonException) { return null; }
    }

    private async Task<EvolvePaymentRequest?> PatchTabaPayIdsAsync(
        TabaPayResponse tabaPayResponse,
        string referenceId,
        EvolvePaymentRequest request)
    {
        var patches = EvolvePaymentRequestHelper.GetTabaPaypatchoperation(
            tabaPayResponse.TransactionId, referenceId, tabaPayResponse.NetworkId);
        return await _paymentCosmosDB.PatchItemAsync(request, patches);
    }

    private async Task<EvolvePaymentRequest?> PatchTransactionStatusAsync(
        RequestStage stage, RequestStatus status, string additionalInfo, EvolvePaymentRequest request)
    {
        var patches = EvolvePaymentRequestHelper.GetStatusPatchOperation(stage, status, additionalInfo);
        return await _paymentCosmosDB.PatchItemAsync(request, patches);
    }
}