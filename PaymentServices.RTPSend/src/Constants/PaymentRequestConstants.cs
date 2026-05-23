namespace PaymentServices.RTPSend.Constants;

public static class PaymentRequestConstants
{
    public const string DocumentType = "CreatePayment";
    public const string DocumentSubTypeTabaPay = "TabaPay";

    public const string TransactionCompleted = "Completed";
    public const string TransactionFailed = "Failed";

    // Service Bus subjects — discriminator on the published envelope
    public const string CreatePaymentRequestSubject = "CreatePaymentRequest";
    public const string SuccessServiceBusSubject = "CreatePayment - Success";
    public const string FailureServiceBusSubject = "CreatePayment - Failure";

    public const string CreatePaymentTypePush = "push";
    public const string CreatePaymentAchOptions = "R";
    public const string CreatePaymentDefaultCurrency = "840";

    public const string TabaPayComplete = "COMPLETED";
}
