using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Models.Response;
using PaymentServices.RTPSend.Settings;

namespace PaymentServices.RTPSend.Services;

/// <summary>
/// Performs account-lookup against the partner ledger:
///   1. Try Cosmos (cached prior lookups).
///   2. Fall back to SQL stored procedure if Cosmos has no record.
///   3. Cache the SQL result back into Cosmos for next time.
/// Patches the payment document with the resolved fboAccount / fintechId / taxId,
/// then throws <see cref="PartnerLedgerException"/> on any unrecoverable error.
///
/// Service-Bus failure publishing is intentionally NOT handled here — the
/// orchestrator catches the exception and handles all side-effects in one place.
/// </summary>
public interface IPartnerLedgerSystem
{
    Task<EvolvePaymentRequest> PerformAccountLookupUpdate(EvolvePaymentRequest request);
}

public sealed class PartnerLedgerSystem : IPartnerLedgerSystem
{
    private readonly IPaymentCosmosDBAdapter _paymentCosmosDB;
    private readonly IPartnerLedgerCosmosDBAdapter _partnerLedgerCosmosDB;
    private readonly RtpSendSettings _settings;
    private readonly ILogger<PartnerLedgerSystem> _logger;

    public PartnerLedgerSystem(
        IPaymentCosmosDBAdapter paymentCosmosDB,
        IPartnerLedgerCosmosDBAdapter partnerLedgerCosmosDB,
        IOptions<RtpSendSettings> settings,
        ILogger<PartnerLedgerSystem> logger)
    {
        _paymentCosmosDB = paymentCosmosDB;
        _partnerLedgerCosmosDB = partnerLedgerCosmosDB;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<EvolvePaymentRequest> PerformAccountLookupUpdate(EvolvePaymentRequest request)
    {
        if (request.SourceAccount is null)
            throw new PartnerLedgerException("SourceAccount is required for partner-ledger lookup.");

        var accountNumber = request.SourceAccount.AccountNumber;
        PartnerLedgerResponse partnerLedgerResponse;

        try
        {
            // 1. Cosmos first
            partnerLedgerResponse = await _partnerLedgerCosmosDB.GetItemAsync(accountNumber);

            // 2. If not found in Cosmos, fall back to SQL
            if (partnerLedgerResponse.ErrorMessage is not null)
            {
                _logger.LogInformation(
                    "Partner ledger not in Cosmos ({Error}); falling back to SQL.",
                    partnerLedgerResponse.ErrorMessage);

                partnerLedgerResponse = await PartnerLedgerVAccountLookup(accountNumber);

                // 3. Cache the SQL result in Cosmos for next time
                if (partnerLedgerResponse is not null && partnerLedgerResponse.ErrorMessage is null)
                {
                    _logger.LogInformation(
                        "Caching partner-ledger entry in Cosmos: {Response}",
                        JsonSerializer.Serialize(partnerLedgerResponse));
                    await _partnerLedgerCosmosDB.CreateItemAsync(partnerLedgerResponse);
                }
            }
        }
        catch (CosmosGetException ex)
        {
            _logger.LogError(ex,
                "Cosmos partner-ledger lookup failed; falling back to SQL. Message: {Message}",
                ex.Message);
            partnerLedgerResponse = await PartnerLedgerVAccountLookup(accountNumber);
        }
        catch (PartnerLedgerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Partner-ledger lookup threw an unexpected exception. Message: {Message}",
                ex.Message);

            await PatchTransactionStatusAsync(
                RequestStage.ACCOUNTLOOKUP, RequestStatus.FAILED, ex.Message, request);

            throw new PartnerLedgerException(ex.Message, ex);
        }

        // Lookup returned no data
        if (partnerLedgerResponse is null || partnerLedgerResponse.ErrorMessage is not null)
        {
            var msg = partnerLedgerResponse?.ErrorMessage ?? "Partner ledger lookup returned no data";
            _logger.LogWarning("Partner Ledger Lookup unsuccessful. Error: {Error}", msg);

            await PatchTransactionStatusAsync(
                RequestStage.ACCOUNTLOOKUP, RequestStatus.FAILED, msg, request);

            throw new PartnerLedgerException(msg);
        }

        // Account closed / blocked => reject
        if (partnerLedgerResponse.AccountStatus is "CLOSED" or "BLOCKED")
        {
            await PatchTransactionStatusAsync(
                RequestStage.ACCOUNTLOOKUP,
                RequestStatus.REJECTED,
                $"Debtor Account Status: {partnerLedgerResponse.AccountStatus}",
                request);

            throw new PartnerLedgerException(
                $"Account {accountNumber} status: {partnerLedgerResponse.AccountStatus}");
        }

        _logger.LogInformation(
            "Partner ledger lookup successful: {Response}",
            JsonSerializer.Serialize(partnerLedgerResponse));

        // Status: ACCOUNTLOOKUP -> COMPLETED
        await PatchTransactionStatusAsync(
            RequestStage.ACCOUNTLOOKUP, RequestStatus.COMPLETED, "PartnerLedgerLookup completed", request);

        // Patch the resolved fields onto the document
        var fieldPatches = EvolvePaymentRequestHelper.SetAccountLookupPatchoperation(partnerLedgerResponse);
        var patched = await _paymentCosmosDB.PatchItemAsync(request, fieldPatches);
        return patched ?? request;
    }

    /// <summary>
    /// Direct SQL stored-proc fallback. The Cosmos cache is the preferred path
    /// — this only fires when Cosmos has no entry for the account number yet.
    /// </summary>
    private async Task<PartnerLedgerResponse> PartnerLedgerVAccountLookup(string vAccount)
    {
        _logger.LogInformation("Executing SP {SpName} with vAccount {VAccount}",
            _settings.PARTNER_LEDGER_SPNAME, vAccount);

        await using var conn = new SqlConnection(_settings.PARTNER_LEDGER_SQL_CONNSTRING);
        var sql = $"exec {_settings.PARTNER_LEDGER_SPNAME} @vAccount";
        var parameters = new { vAccount };

        var response = (await conn.QueryAsync<PartnerLedgerResponse>(sql, parameters)).FirstOrDefault();

        _logger.LogInformation("Response from Partner Ledger SP: {Response}",
            JsonSerializer.Serialize(response));

        return response ?? new PartnerLedgerResponse { ErrorMessage = "Partner ledger SP returned no rows" };
    }

    private async Task<EvolvePaymentRequest?> PatchTransactionStatusAsync(
        RequestStage stage, RequestStatus status, string additionalInfo, EvolvePaymentRequest request)
    {
        var patches = EvolvePaymentRequestHelper.GetStatusPatchOperation(stage, status, additionalInfo);
        return await _paymentCosmosDB.PatchItemAsync(request, patches);
    }
}