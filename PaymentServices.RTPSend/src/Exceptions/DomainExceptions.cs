namespace PaymentServices.RTPSend.Exceptions;

public class PartnerLedgerException : Exception
{
    public PartnerLedgerException(string message) : base(message) { }
    public PartnerLedgerException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>Thrown when LimitService rejects the payment (e.g. daily / per-txn limit exceeded).</summary>
public class LimitExceededException : Exception
{
    public LimitExceededException(string message) : base(message) { }
    public LimitExceededException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>Thrown when LedgerService cannot reserve funds (e.g. insufficient balance).</summary>
public class LedgerReservationException : Exception
{
    public LedgerReservationException(string message) : base(message) { }
    public LedgerReservationException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>Thrown when TabaPay returns a non-success response or the HTTP call itself fails.</summary>
public class TabaPayProcessingException : Exception
{
    public TabaPayProcessingException(string message) : base(message) { }
    public TabaPayProcessingException(string message, Exception? inner) : base(message, inner) { }
}

public class CosmosGetException : Exception
{
    public CosmosGetException(string message) : base(message) { }
    public CosmosGetException(string message, Exception? inner) : base(message, inner) { }
}
