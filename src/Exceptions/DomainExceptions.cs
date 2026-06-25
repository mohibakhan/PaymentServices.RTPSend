using System.Net;

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
    /// <summary>
    /// True when the failure is worth retrying (5xx, timeout, network). False for
    /// deterministic failures (4xx validation, hard declines) that would fail
    /// identically on every retry — those are dead-lettered rather than redelivered.
    /// Defaults to true so callers that don't classify keep the old retry behaviour.
    /// </summary>
    public bool IsRetryable { get; }

    /// <summary>TabaPay HTTP status when the failure came from a response; null for transport faults.</summary>
    public HttpStatusCode? StatusCode { get; }

    public TabaPayProcessingException(string message, bool isRetryable = true, HttpStatusCode? statusCode = null)
        : base(message)
    {
        IsRetryable = isRetryable;
        StatusCode = statusCode;
    }

    public TabaPayProcessingException(string message, Exception? inner, bool isRetryable = true, HttpStatusCode? statusCode = null)
        : base(message, inner)
    {
        IsRetryable = isRetryable;
        StatusCode = statusCode;
    }
}

public class CosmosGetException : Exception
{
    public CosmosGetException(string message) : base(message) { }
    public CosmosGetException(string message, Exception? inner) : base(message, inner) { }
}
