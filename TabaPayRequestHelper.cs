using System.Security.Cryptography;
using System.Text;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Helpers;

public static class TabaPayRequestHelper
{
    public static TabapayPaymentRequest ConvertEvolveToTabaPayRequest(
        EvolvePaymentRequest evolve,
        string sourceAccountId)
    {
        var request = new TabapayPaymentRequest
        {
            // Deterministic from evolveId so SB redeliveries don't generate
            // new referenceIDs (which would let TabaPay process the same
            // logical payment twice).
            ReferenceId = DeriveReferenceId(evolve.EvolveId),
            Type = evolve.Type ?? PaymentRequestConstants.CreatePaymentTypePush,
            AchOptions = evolve.AchOptions ?? PaymentRequestConstants.CreatePaymentAchOptions,
            Amount = evolve.Amount,
            Currency = evolve.PaymentCurrency ?? PaymentRequestConstants.CreatePaymentDefaultCurrency,
            Corresponding = evolve.SourceAccount is null ? null : new Corresponding
            {
                Name = evolve.SourceAccount.Name,
                Address = (TabapayAddress?)evolve.SourceAccount.Address,
                AccountNumber = evolve.SourceAccount.AccountNumber,
                SourceOfFunds = MapSourceOfFunds(evolve.SourceAccount.AccountType)
            },
            Accounts = new Accounts
            {
                SourceAccountId = sourceAccountId,
                DestinationAccount = evolve.DestinationAccount is null ? null : new Account
                {
                    Bank = new Bank
                    {
                        AccountNumber = evolve.DestinationAccount.AccountNumber,
                        RoutingNumber = evolve.DestinationAccount.RoutingNumber,
                        AccountType = evolve.DestinationAccount.AccountType
                    },
                    Owner = new Owner
                    {
                        Name = evolve.DestinationAccount.Name,
                        Address = (TabapayAddress?)evolve.DestinationAccount.Address,
                        Phone = new Phone
                        {
                            Number = evolve.DestinationAccount.PhoneNumber
                        }
                    }
                }
            }
        };

        if (evolve.SoftDescriptor is not null)
            request.SoftDescriptor = evolve.SoftDescriptor;

        if (!string.IsNullOrWhiteSpace(evolve.RemittanceInformation))
            request.Memo = evolve.RemittanceInformation;

        return request;
    }

    /// <summary>
    /// Maps a bank account type code (single char: S/C/A/B/L) to TabaPay's
    /// sourceOfFunds descriptor. Used in the Corresponding block of an RTP
    /// push request. Falls back to "Credit Account" for unknown / empty
    /// values to preserve historic behavior.
    /// </summary>
    private static string MapSourceOfFunds(string? accountType) =>
        accountType?.ToUpperInvariant() switch
        {
            "S" => "Savings Account",          // Savings
            "C" => "Checking Account",         // Checking
            "A" => "Savings Account",          // Business Savings
            "B" => "Checking Account",         // Business Checking
            "L" => "Credit Account",           // Loan
            _   => "Credit Account"            // default / unknown
        };

    /// <summary>
    /// Derives a deterministic 15-character referenceID from the evolveId.
    /// Same evolveId always produces the same referenceID, so Service Bus
    /// redeliveries (after a transient TabaPay failure) send the same
    /// referenceID — TabaPay then sees the retry as a duplicate of the
    /// original request rather than a new, independent transaction.
    ///
    /// Encoding: first 15 chars of base32(SHA-256(evolveId)).
    ///   - 15 chars × 5 bits each = 75 bits of entropy
    ///   - Collision probability at 3M txns/month: negligible
    ///   - Base32 RFC 4648 char set (A-Z, 2-7) is alphanumeric-safe for TabaPay
    /// </summary>
    public static string DeriveReferenceId(string evolveId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evolveId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(evolveId));
        return Base32EncodeFirst15(hash);
    }

    private static string Base32EncodeFirst15(byte[] bytes)
    {
        // RFC 4648 base32 alphabet (no padding)
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        // 15 base32 chars need 75 bits. Read 10 bytes (80 bits), emit 15 chars.
        // We don't pad — slice exactly 15 chars off the front.
        Span<char> result = stackalloc char[15];

        int bitBuffer = 0;
        int bitCount = 0;
        int byteIndex = 0;
        int outputIndex = 0;

        while (outputIndex < 15)
        {
            // Refill bit buffer from next input byte
            while (bitCount < 5 && byteIndex < bytes.Length)
            {
                bitBuffer = (bitBuffer << 8) | bytes[byteIndex++];
                bitCount += 8;
            }

            // Extract top 5 bits
            int alphabetIndex = (bitBuffer >> (bitCount - 5)) & 0x1F;
            bitCount -= 5;
            result[outputIndex++] = alphabet[alphabetIndex];
        }

        return new string(result);
    }
}
