namespace PaymentServices.RTPSend.Constants;

public static class PaymentRequestConstants
{
    public const string DocumentType = "CreatePayment";
    public const string DocumentSubTypeTabaPay = "TabaPay";

    public const string TransactionCompleted = "Completed";
    public const string TransactionFailed = "Failed";

    // Service Bus — platform-fixed names (do not vary by environment)
    public const string ServiceBusTopicName = "payment-processing";
    public const string ServiceBusProcessSubscriptionName = "rtpsend-process";

    // Dedicated queue for backed-off TabaPay send retries (transient failures).
    // Owned by this service; must exist in the Service Bus namespace.
    public const string TabaPayRetryQueueName = "rtpsend-tabapay-retry";

    // Service Bus subjects — discriminator on the published envelope
    public const string CreatePaymentRequestSubject = "CreatePaymentRequest";
    public const string SuccessServiceBusSubject = "CreatePayment - Success";
    public const string FailureServiceBusSubject = "CreatePayment - Failure";

    public const string CreatePaymentTypePush = "push";
    public const string CreatePaymentAchOptions = "R";
    public const string CreatePaymentDefaultCurrency = "840";

    public const string TabaPayComplete = "COMPLETED";
}