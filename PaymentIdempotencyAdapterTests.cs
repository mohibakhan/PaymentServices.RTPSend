using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Repositories;

namespace PaymentServices.RTPSend.UnitTests.Repositories;

public class PaymentIdempotencyAdapterTests
{
    private readonly Mock<Container> _container = new();
    private readonly PaymentIdempotencyAdapter _sut;

    public PaymentIdempotencyAdapterTests()
    {
        _sut = new PaymentIdempotencyAdapter(
            _container.Object,
            Mock.Of<ILogger<PaymentIdempotencyAdapter>>());
    }

    [Fact]
    public async Task TryReserveAsync_WhenInsertSucceeds_ReturnsTrue()
    {
        var entry = NewEntry("ref-success");

        _container
            .Setup(c => c.CreateItemAsync(
                entry,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<PaymentIdempotencyEntry>>());

        var result = await _sut.TryReserveAsync(entry);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryReserveAsync_WhenCosmosReturns409_ReturnsFalse()
    {
        var entry = NewEntry("ref-duplicate");

        _container
            .Setup(c => c.CreateItemAsync(
                entry,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeCosmosException(HttpStatusCode.Conflict));

        var result = await _sut.TryReserveAsync(entry);

        result.Should().BeFalse("a 409 means the paymentReference is already reserved");
    }

    [Fact]
    public async Task TryReserveAsync_WhenCosmosReturns500_PropagatesException()
    {
        var entry = NewEntry("ref-server-error");

        _container
            .Setup(c => c.CreateItemAsync(
                entry,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeCosmosException(HttpStatusCode.InternalServerError));

        await FluentActions.Invoking(() => _sut.TryReserveAsync(entry))
            .Should().ThrowAsync<CosmosException>()
            .Where(ce => ce.StatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task TryReserveAsync_UsesPaymentReferenceAsPartitionKey()
    {
        var entry = NewEntry("ref-partition-check");
        PartitionKey? captured = null;

        _container
            .Setup(c => c.CreateItemAsync(
                entry,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<PaymentIdempotencyEntry, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (_, pk, _, _) => captured = pk)
            .ReturnsAsync(Mock.Of<ItemResponse<PaymentIdempotencyEntry>>());

        await _sut.TryReserveAsync(entry);

        captured.Should().Be(new PartitionKey("ref-partition-check"));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static PaymentIdempotencyEntry NewEntry(string paymentReference) => new()
    {
        Id = paymentReference,
        PaymentReference = paymentReference,
        EvolveId = Guid.NewGuid().ToString(),
        CreatedAt = DateTime.UtcNow.ToString("O")
    };

    private static CosmosException MakeCosmosException(HttpStatusCode status) =>
        new(
            message: $"simulated {status}",
            statusCode: status,
            subStatusCode: 0,
            activityId: Guid.NewGuid().ToString(),
            requestCharge: 0);
}
