namespace PaymentServices.RTPSend.Settings;

/// <summary>
/// RTPSend-specific configuration. Bound from the <c>rtpSend:AppSettings</c> section.
/// Shared infrastructure settings (Cosmos endpoint/database, Service Bus connection)
/// live in <c>PaymentServices.Shared.Models.AppSettings</c> and are registered via
/// <c>services.AddPaymentAppSettings(config, "rtpSend:AppSettings")</c>.
/// </summary>
public class RtpSendSettings
{
    // -------------------------------------------------------------------------
    // Cosmos — container names (database name comes from the shared AppSettings)
    // -------------------------------------------------------------------------
    public string COSMOS_PAYMENT_CONTAINER { get; set; } = string.Empty;
    public string COSMOS_PARTNER_LEDGER_CONTAINER { get; set; } = string.Empty;
    public string COSMOS_API_CONFIG_CONTAINER { get; set; } = string.Empty;
    public string COSMOS_IDEMPOTENCY_CONTAINER { get; set; } = string.Empty;

    // -------------------------------------------------------------------------
    // TabaPay
    // -------------------------------------------------------------------------
    public string TABAPAY_SEND_URL { get; set; } = string.Empty;
    public string TABAPAY_SEND_APIKEY { get; set; } = string.Empty;
    public string TABAPAY_SEND_CLIENT_ID { get; set; } = string.Empty;
    public string TABAPAY_SEND_MERCHANT_ID { get; set; } = string.Empty;

    // -------------------------------------------------------------------------
    // Service Bus
    //
    // The TOPIC and SUBSCRIPTION names are platform-fixed (payment-processing /
    // rtpsend-process) and live as constants in PaymentRequestConstants — they
    // don't vary across environments and aren't configurable.
    //
    // The CONNECTION STRING for runtime publishers comes from the shared
    // AppSettings (rtpSend:AppSettings:SERVICE_BUS_CONNSTRING).
    //
    // The CONNECTION STRING for the ProcessPayment trigger binding must be set
    // as a flat Function App setting named SERVICE_BUS_CONNSTRING — isolated
    // worker can't resolve binding connections from App Configuration.
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // SQL — Partner ledger stored procedure
    // -------------------------------------------------------------------------
    public string PARTNER_LEDGER_SQL_CONNSTRING { get; set; } = string.Empty;
    public string PARTNER_LEDGER_SPNAME { get; set; } = string.Empty;

    // -------------------------------------------------------------------------
    // RTP
    // -------------------------------------------------------------------------
    public string RTP_SEND_TRAN_CODE { get; set; } = string.Empty;

    // -------------------------------------------------------------------------
    // Retry timer — CRON expression for the DLQ-drain function
    // -------------------------------------------------------------------------
    public string RETRY_TIMER_SCHEDULE { get; set; } = "0 */5 * * * *"; // every 5 minutes
}
