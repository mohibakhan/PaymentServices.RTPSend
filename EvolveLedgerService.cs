using Evolve.Digital.LedgerService.Shared.Internal;
using Evolve.Digital.LedgerService.Shared.Internal.Models;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Interface.External;

namespace PaymentServices.RTPSend.Services;

/// <summary>
/// Implements RTPSend's <see cref="ILedgerService"/> by delegating to the
/// platform-wide <see cref="ILedgerInternalClient"/> from the
/// Evolve.Digital.LedgerService.Shared.Internal NuGet package.
///
/// Flow for an RTP send:
///   1. Validate the amount parses to a decimal
///   2. Look up the FBO ledger by account number
///   3. NSF check — would this debit push the ledger negative?
///      If yes → throw <see cref="InsufficientFundsException"/> (TERMINAL)
///   4. Post a debit entry (negative amount) tagged with our evolveId
///
/// NSF is a terminal failure — Service Bus must NOT retry. The orchestrator
/// translates other ledger failures into <see cref="LedgerReservationException"/>
/// (which DOES retry); only NSF skips the retry path.
/// </summary>
public sealed class EvolveLedgerService : ILedgerService
{
    private const string LedgerEntryKind = "rtp.send";

    private readonly ILedgerInternalClient _ledgerClient;
    private readonly ILogger<EvolveLedgerService> _logger;

    public EvolveLedgerService(
        ILedgerInternalClient ledgerClient,
        ILogger<EvolveLedgerService> logger)
    {
        _ledgerClient = ledgerClient;
        _logger = logger;
    }

    public async Task<LedgerReservationResult> ReserveAsync(
        LedgerReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate amount
        if (!decimal.TryParse(request.Amount, out var amountDecimal))
        {
            _logger.LogError(
                "Invalid amount '{Amount}' for evolveId {EvolveId}",
                request.Amount, request.EvolveId);
            return LedgerReservationResult.Failed(
                $"Amount '{request.Amount}' is not a valid decimal");
        }

        // 2. Look up the FBO ledger by account number
        var ledger = await _ledgerClient.GetLedgerByAccountAsync(request.FboAccountNumber);
        if (ledger is null)
        {
            _logger.LogError(
                "Ledger not found for FBO account {FboAccountNumber} (evolveId {EvolveId})",
                request.FboAccountNumber, request.EvolveId);
            return LedgerReservationResult.Failed(
                $"Ledger not found for account {request.FboAccountNumber}");
        }

        // 3. NSF check — terminal failure if insufficient funds
        var nsf = await _ledgerClient.CheckNsfAsync(ledger.id, amountDecimal);
        if (nsf.ProjectedBalance < 0)
        {
            _logger.LogWarning(
                "Insufficient funds on ledger {LedgerId} (evolveId {EvolveId}): balance={Balance}, requested={Amount}, projected={Projected}",
                ledger.id, request.EvolveId, nsf.Balance, amountDecimal, nsf.ProjectedBalance);

            throw new InsufficientFundsException(
                currentBalance: nsf.Balance,
                requestedAmount: amountDecimal,
                projectedBalance: nsf.ProjectedBalance,
                message: $"Insufficient funds on account {request.FboAccountNumber}: " +
                         $"balance {nsf.Balance:F2}, requested {amountDecimal:F2}");
        }

        // 4. Post a debit entry (negative amount) tagged with our evolveId
        var metadata = new Dictionary<string, object>
        {
            { "evolveId", request.EvolveId },
            { "paymentReference", request.PaymentReference },
            { "clientId", request.ClientId },
            { "merchantId", request.MerchantId },
            { "fintechId", request.FintechId },
            { "Account", request.FboAccountNumber },
            { "endpoint", "rtpsend" }
        };

        var addEntryRequest = new AddEntryRequest(
            LedgerId: ledger.id,
            Amount: -amountDecimal,                       // debit — negative
            Trace: new { evolveId = request.EvolveId },
            Kind: LedgerEntryKind,                        // queried by settlement
            Metadata: metadata,
            IsRemoteAccount: false);

        try
        {
            var entryId = await _ledgerClient.AddEntryAsync(addEntryRequest);

            _logger.LogInformation(
                "Ledger entry {EntryId} posted on ledger {LedgerId} for evolveId {EvolveId} amount {Amount}",
                entryId, ledger.id, request.EvolveId, -amountDecimal);

            return LedgerReservationResult.Ok(entryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to post ledger entry for evolveId {EvolveId} on ledger {LedgerId}",
                request.EvolveId, ledger.id);

            return LedgerReservationResult.Failed(
                $"AddEntry failed on ledger {ledger.id}: {ex.Message}");
        }
    }
}
