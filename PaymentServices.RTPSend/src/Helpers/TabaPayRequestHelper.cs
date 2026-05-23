using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Helpers;

public static class TabaPayRequestHelper
{
    public static TabapayPaymentRequest ConvertEvolveToTabaPayRequest(EvolvePaymentRequest evolve)
    {
        var request = new TabapayPaymentRequest
        {
            ReferenceId = GenerateReferenceId(),
            Type = evolve.Type ?? PaymentRequestConstants.CreatePaymentTypePush,
            AchOptions = evolve.AchOptions ?? PaymentRequestConstants.CreatePaymentAchOptions,
            Amount = evolve.Amount,
            Currency = evolve.PaymentCurrency ?? PaymentRequestConstants.CreatePaymentDefaultCurrency,
            Corresponding = evolve.SourceAccount is null ? null : new Corresponding
            {
                Name = evolve.SourceAccount.Name,
                Address = (TabapayAddress?)evolve.SourceAccount.Address,
                AccountNumber = evolve.SourceAccount.AccountNumber,
                SourceOfFunds = "Credit Account"
            },
            Accounts = new Accounts
            {
                SourceAccountId = "yuEAY8eEwGafmufciZBrEQ", // TODO: source from config / per-merchant lookup
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

        return request;
    }

    private static string GenerateReferenceId()
    {
        const string allowed = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[15];
        for (var i = 0; i < 15; i++)
            chars[i] = allowed[Random.Shared.Next(allowed.Length)];
        return new string(chars);
    }
}
