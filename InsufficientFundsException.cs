namespace PaymentServices.RTPSend.Exceptions;

/// <summary>
/// Thrown when the source ledger doesn't have enough funds to cover the
/// payment. This is a TERMINAL failure — Service Bus should NOT retry,
/// because the balance won't change via retry. ProcessPayment catches this
/// specifically and completes the message instead of throwing through to
/// the SB pipeline.
///
/// Resolution requires either the customer topping up the FBO account or
/// the payment being abandoned.
/// </summary>
public sealed class InsufficientFundsException : Exception
{
    public decimal CurrentBalance { get; }
    public decimal RequestedAmount { get; }
    public decimal ProjectedBalance { get; }

    public InsufficientFundsException(
        decimal currentBalance,
        decimal requestedAmount,
        decimal projectedBalance,
        string message)
        : base(message)
    {
        CurrentBalance = currentBalance;
        RequestedAmount = requestedAmount;
        ProjectedBalance = projectedBalance;
    }
}
