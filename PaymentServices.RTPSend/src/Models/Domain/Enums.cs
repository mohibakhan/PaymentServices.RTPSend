namespace PaymentServices.RTPSend.Models.Domain;

public enum RequestStatus
{
    RECEIVED,
    ACCEPTED,
    INITIATED,
    COMPLETED,
    REJECTED,
    PENDING,
    RELEASED,
    SETTLED,
    RETURNED,
    CANCELED,
    ACTION_REQUIRED,
    FAILED
}

public enum RequestStage
{
    UNKNOWN,
    // Pipeline stages — order matters: int values are used by
    // PaymentOrchestrator to decide which stages still need to run.
    RTP_API,         // payment received, not yet processed
    ACCOUNTLOOKUP,   // partner-ledger lookup
    LIMIT,           // LimitService check
    LEDGER,          // LedgerService reservation
    TABAPAY,         // TabaPay call
    // Non-pipeline / terminal-only stages
    POSTING,
    JHA,
    WIRE,
    TRANSFERMATE
}

/// <summary>
/// Bank account type. S - Savings, C - Checking, A - Business Savings,
/// B - Business Checking, L - Loan.
/// Kept as an enum for FluentValidation EnumDataType compatibility.
/// </summary>
public enum AccountType
{
    S,
    C,
    A,
    B,
    L
}
