namespace PaymentServices.RTPSend.Exceptions;

/// <summary>
/// Thrown when the source ledger doesn't have enough funds to cover the
/// payment. This is a TERMINAL failure — Service Bus should NOT retry,
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