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
/// outcome, return a <see cref="TabaPaySendResult"/>. No Service Bus publishing,
/// no API user-config lookups — orchestrator handles all that.
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
        _logger.LogInformation("TabaPay processing for evolveId {EvolveId}", cosmosPaymentItem.EvolveId);

        var tabapayRequest = TabaPayRequestHelper.ConvertEvolveToTabaPayRequest(cosmosPaymentItem);

        HttpResponseMessage httpResponse;
        string rawBody;

        try
        {
            httpResponse = await SendTransactionAsync(tabapayRequest);
            rawBody = await httpResponse.Content.ReadAsStringAsync();
            _logger.LogInformation("TabaPay raw response ({Status}): {Body}", httpResponse.StatusCode, rawBody);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "TabaPay HTTP call failed: {Message}", ex.Message);
            await PatchTransactionStatusAsync(
                RequestStage.TABAPAY, RequestStatus.FAILED, $"HTTP error: {ex.Message}", cosmosPaymentItem);
            throw new TabaPayProcessingException($"TabaPay HTTP call failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "TabaPay HTTP call timed out.");
            await PatchTransactionStatusAsync(
                RequestStage.TABAPAY, RequestStatus.FAILED, "Timeout calling TabaPay", cosmosPaymentItem);
            throw new TabaPayProcessingException("TabaPay HTTP call timed out", ex);
        }

        var tabaPayResponse = TryDeserialize(rawBody);
        var isSuccess =
            (int)httpResponse.StatusCode == (int)HttpStatusCode.OK
            && tabaPayResponse?.Status == PaymentRequestConstants.TabaPayComplete
            && tabaPayResponse.Sc == (int)HttpStatusCode.OK;

        await PatchTransactionStatusAsync(
            RequestStage.TABAPAY,
            isSuccess ? RequestStatus.COMPLETED : RequestStatus.FAILED,
            rawBody,
            cosmosPaymentItem);

        if (!isSuccess)
        {
            _logger.LogWarning("TabaPay returned non-success. Status: {Status}, Body: {Body}",
                httpResponse.StatusCode, rawBody);
            throw new TabaPayProcessingException(
                $"TabaPay returned non-success. HTTP status: {httpResponse.StatusCode}. Response: {rawBody}");
        }

        // Patch transaction IDs onto the Cosmos document
        var patched = await PatchTabaPayIdsAsync(tabaPayResponse!, tabapayRequest.ReferenceId, cosmosPaymentItem);

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
        _logger.LogInformation("TabaPay request body: {Body}", json);

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
