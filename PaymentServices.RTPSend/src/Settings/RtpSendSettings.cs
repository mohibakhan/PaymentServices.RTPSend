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
    // Service Bus — channels
    //
    // SERVICE_BUS_TOPIC_NAME                — shared platform topic. Used for:
    //                                          - Inbound: CreatePayment publishes
    //                                            here with Subject = 'CreatePaymentRequest'
    //                                            so the ProcessPayment subscription
    //                                            picks it up.
    //                                          - Outbound: ProcessPayment publishes
    //                                            terminal outcomes (success/failure)
    //                                            here with Subject = 'CreatePayment - Success'
    //                                            or 'CreatePayment - Failure' for
    //                                            downstream subscribers.
    //
    // SERVICE_BUS_PROCESS_SUBSCRIPTION_NAME — RTPSend's subscription on the topic.
    //                                          ProcessPayment triggers off this.
    //                                          Its $DeadLetterQueue is drained by
    //                                          RetryFailedPayments.
    //
    // The connection string lives in the shared AppSettings.
    // -------------------------------------------------------------------------
    public string SERVICE_BUS_TOPIC_NAME { get; set; } = string.Empty;
    public string SERVICE_BUS_PROCESS_SUBSCRIPTION_NAME { get; set; } = string.Empty;

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
