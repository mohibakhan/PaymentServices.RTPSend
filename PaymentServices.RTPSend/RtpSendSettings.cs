namespace PaymentServices.RTPSend.Settings;

/// <summary>
/// RTPSend-specific configuration. Bound from the <c>rtpSend:AppSettings</c> section.
/// </summary>
public class RtpSendSettings
{
    // Cosmos — container names (database name comes from the shared AppSettings)
    public string COSMOS_PAYMENT_CONTAINER { get; set; } = string.Empty;
    public string COSMOS_PARTNER_LEDGER_CONTAINER { get; set; } = string.Empty;
    public string COSMOS_API_CONFIG_CONTAINER { get; set; } = string.Empty;
    public string COSMOS_IDEMPOTENCY_CONTAINER { get; set; } = string.Empty;


    // TabaPay
    public string TABAPAY_SEND_URL { get; set; } = string.Empty;
    public string TABAPAY_SEND_APIKEY { get; set; } = string.Empty;
    public string TABAPAY_SEND_CLIENT_ID { get; set; } = string.Empty;
    public string TABAPAY_SEND_MERCHANT_ID { get; set; } = string.Empty;
    public string TABAPAY_SOURCE_ACCOUNT_ID { get; set; } = string.Empty;


    // SQL — Partner ledger stored procedure
    public string PARTNER_LEDGER_SQL_CONNSTRING { get; set; } = string.Empty;
    public string PARTNER_LEDGER_SPNAME { get; set; } = string.Empty;

    // Ledgers cosmos database
    public string LEDGER_COSMOS_DATABASE { get; set; } = string.Empty;


    // RTP
    public string RTP_SEND_TRAN_CODE { get; set; } = string.Empty;

    // -------------------------------------------------------------------------
    // Gateway
    // -------------------------------------------------------------------------

    /// <summary>Full URL of the Gateway tptch/send endpoint (e.g.
    /// https://fa-pmtsvc-gateway-{env}.azurewebsites.net/api/tptch/send).</summary>
    public string GATEWAY_TPTCH_SEND_URL { get; set; } = string.Empty;

    /// <summary>Function key for the Gateway tptch/send endpoint (x-functions-key).</summary>
    public string GATEWAY_TPTCH_SEND_APIKEY { get; set; } = string.Empty;
    
}