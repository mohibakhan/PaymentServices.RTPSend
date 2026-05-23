using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.UnitTests.TestHelpers;

/// <summary>
/// Fluent builders for test fixtures. Keeps tests readable; avoids 50-line
/// object initializers everywhere.
/// </summary>
internal static class TestDataBuilder
{
    public static BasicPaymentRequest AValidBasicRequest(string? paymentReference = null) =>
        new()
        {
            PaymentReference = paymentReference ?? Guid.NewGuid().ToString(),
            Amount = "1.00",
            SourceAccount = new SourceAccount
            {
                AccountNumber = "9010010000000001",
                RoutingNumber = "084009593",
                AccountType = "S",
                Name = new AccountName { First = "Sender", Last = "Test" }
            },
            DestinationAccount = new DestinationAccount
            {
                AccountNumber = "900397187386253",
                RoutingNumber = "101115315",
                AccountType = "C",
                Name = new AccountName { First = "Receiver", Last = "Test" }
            }
        };

    public static EvolvePaymentRequest AnEvolvePaymentAtStage(
        RequestStage stage = RequestStage.RTP_API,
        RequestStatus status = RequestStatus.RECEIVED,
        string? evolveId = null,
        string? paymentReference = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            EvolveId = evolveId ?? Guid.NewGuid().ToString(),
            PaymentReference = paymentReference ?? Guid.NewGuid().ToString(),
            Amount = "1.00",
            Stage = stage.ToString(),
            Status = status.ToString(),
            ClientId = "test-client",
            MerchantId = "test-merchant",
            FintechId = "test-fintech",
            FboAccountNumber = "fbo-123",
            SourceAccount = new SourceAccount
            {
                AccountNumber = "9010010000000001",
                RoutingNumber = "084009593",
                AccountType = "S",
                Name = new AccountName { First = "Sender", Last = "Test" }
            },
            DestinationAccount = new DestinationAccount
            {
                AccountNumber = "900397187386253",
                RoutingNumber = "101115315",
                AccountType = "C",
                Name = new AccountName { First = "Receiver", Last = "Test" }
            }
        };
}
