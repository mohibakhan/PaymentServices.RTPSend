using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace PaymentServices.RTPSend.Functions;

// =============================================================================
// *** TEMPORARY STUB — DELETE BEFORE PRODUCTION ***
//
// Mimics the TabaPay Card endpoint so load tests don't hammer the TabaPay
// sandbox (which 429s under load). Always returns HTTP 200 with a COMPLETED
// response after a 1.5–2s delay to roughly mimic TabaPay latency.
//
// To use: point rtpSend:AppSettings:TABAPAY_SEND_URL at this function's URL,
// e.g. https://fa-pmtsvc-rtpsend-qa-centralus.azurewebsites.net/api/stub/tabapay
// (include the function key as ?code=... or x-functions-key, since AuthLevel
// is Function — TabaPaySendService doesn't send a function key, so see NOTE).
//
// NOTE on auth: TabaPaySendService sends x-Client-Id / x-merchant-id /
// Ocp-Apim-Subscription-Key headers but NOT an Azure Functions key. So this
// stub uses AuthLevel.Anonymous to accept that call as-is. Anonymous is fine
// for a throwaway QA stub; just remember it's open while it exists.
// =============================================================================
public class StubTabaPay
{
    private static readonly Random _rng = new();
    private readonly ILogger<StubTabaPay> _logger;

    public StubTabaPay(ILogger<StubTabaPay> logger) => _logger = logger;

    [Function("StubTabaPay")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthLevel.Anonymous, "post", Route = "stub/tabapay")] HttpRequestData req)
    {
        // Mimic TabaPay latency: 1.5–2.0 seconds.
        var delayMs = _rng.Next(1500, 2001);
        _logger.LogInformation("StubTabaPay: delaying {DelayMs}ms then returning COMPLETED.", delayMs);
        await Task.Delay(delayMs);

        // Shape must match TabaPayResponse (SC/EC/transactionID/network/networkRC/
        // networkID/status/approvalCode). Success criteria in TabaPaySendService:
        // HTTP 200 + status == "COMPLETED" + SC == 200.
        var body = new
        {
            SC = 200,
            EC = "0",
            transactionID = RandomId(22),
            network = "RTP",
            networkRC = "000",
            networkID = $"{DateTime.UtcNow:yyyyMMdd}STUBTABA{_rng.Next(100000, 999999)}",
            status = "COMPLETED",
            approvalCode = _rng.Next(100000, 999999).ToString()
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body));
        return response;
    }

    private static string RandomId(int len)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, len).Select(_ => chars[_rng.Next(chars.Length)]).ToArray());
    }
}
