using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Interface.External;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Models.Response;
using PaymentServices.RTPSend.Services;
using PaymentServices.RTPSend.Settings;
using PaymentServices.RTPSend.UnitTests.TestHelpers;

namespace PaymentServices.RTPSend.UnitTests.Services;

public class PaymentOrchestratorTests
{
    private readonly Mock<IPartnerLedgerSystem> _partnerLedger = new();
    private readonly Mock<ILimitService> _limitService = new();
    private readonly Mock<ILedgerService> _ledgerService = new();
    private readonly Mock<ITabaPaySendService> _tabaPay = new();
    private readonly Mock<IServiceBusMessageService> _serviceBus = new();
    private readonly PaymentOrchestrator _sut;

    public PaymentOrchestratorTests()
    {
        // Default happy-path setups — each test can override
        _partnerLedger
            .Setup(p => p.PerformAccountLookupUpdate(It.IsAny<EvolvePaymentRequest>()))
            .ReturnsAsync((EvolvePaymentRequest p) => p);

        _limitService
            .Setup(l => l.CheckAsync(It.IsAny<LimitCheckRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LimitCheckResult.Ok());

        _ledgerService
            .Setup(l => l.ReserveAsync(It.IsAny<LedgerReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LedgerReservationResult.Ok("reservation-1"));

        _tabaPay
            .Setup(t => t.ProcessPayment(It.IsAny<EvolvePaymentRequest>()))
            .ReturnsAsync((EvolvePaymentRequest p) => new TabaPaySendResult
            {
                Document = p,
                Response = new TabaPayResponse { Sc = 200, Status = "COMPLETED" },
                RawResponse = "{}"
            });

        _serviceBus
            .Setup(s => s.SendMessageToServiceBusAsync(
                It.IsAny<ServiceBusContentModel>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _sut = new PaymentOrchestrator(
            _partnerLedger.Object,
            _limitService.Object,
            _ledgerService.Object,
            _tabaPay.Object,
            _serviceBus.Object,
            Options.Create(new RtpSendSettings()),
            Mock.Of<ILogger<PaymentOrchestrator>>());
    }

    // -------------------------------------------------------------------------
    // ProcessAsync — runs all stages in order
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_RunsAllStagesInOrder()
    {
        var payment = TestDataBuilder.AnEvolvePaymentAtStage(RequestStage.RTP_API);

        await _sut.ProcessAsync(payment);

        _partnerLedger.Verify(p => p.PerformAccountLookupUpdate(payment), Times.Once);
        _limitService.Verify(l => l.CheckAsync(It.IsAny<LimitCheckRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _ledgerService.Verify(l => l.ReserveAsync(It.IsAny<LedgerReservationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _tabaPay.Verify(t => t.ProcessPayment(payment), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OnSuccess_PublishesSuccessEnvelope()
    {
        var payment = TestDataBuilder.AnEvolvePaymentAtStage(RequestStage.RTP_API);

        await _sut.ProcessAsync(payment);

        _serviceBus.Verify(s => s.SendMessageToServiceBusAsync(
                It.IsAny<ServiceBusContentModel>(),
                PaymentRequestConstants.SuccessServiceBusSubject),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // ResumeFromAsync — stage-aware resume logic
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResumeFromAsync_WhenStageIsTABAPAY_OnlyCallsTabaPay()
    {
        var payment = TestDataBuilder.AnEvolvePaymentAtStage(
            stage: RequestStage.TABAPAY, status: RequestStatus.FAILED);

        await _sut.ResumeFromAsync(payment);

        _partnerLedger.Verify(p => p.PerformAccountLookupUpdate(It.IsAny<EvolvePaymentRequest>()), Times.Never);
        _limitService.Verify(l => l.CheckAsync(It.IsAny<LimitCheckRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _ledgerService.Verify(l => l.ReserveAsync(It.IsAny<LedgerReservationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _tabaPay.Verify(t => t.ProcessPayment(payment), Times.Once);
    }

    [Fact]
    public async Task ResumeFromAsync_WhenStageIsLEDGER_SkipsAccountLookupAndLimit()
    {
        var payment = TestDataBuilder.AnEvolvePaymentAtStage(
            stage: RequestStage.LEDGER, status: RequestStatus.FAILED);

        await _sut.ResumeFromAsync(payment);

        _partnerLedger.Verify(p => p.PerformAccountLookupUpdate(It.IsAny<EvolvePaymentRequest>()), Times.Never);
        _limitService.Verify(l => l.CheckAsync(It.IsAny<LimitCheckRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _ledgerService.Verify(l => l.ReserveAsync(It.IsAny<LedgerReservationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _tabaPay.Verify(t => t.ProcessPayment(payment), Times.Once);
    }

    [Fact]
    public async Task ResumeFromAsync_WhenStageIsLIMIT_SkipsAccountLookupOnly()
    {
        var payment = TestDataBuilder.AnEvolvePaymentAtStage(
            stage: RequestStage.LIMIT, status: RequestStatus.FAILED);

        await _sut.ResumeFromAsync(payment);

        _partnerLedger.Verify(p => p.PerformAccountLookupUpdate(It.IsAny<EvolvePaymentRequest>()), Times.Never);
        _limitService.Verify(l => l.CheckAsync(It.IsAny<LimitCheckRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _ledgerService.Verify(l => l.ReserveAsync(It.IsAny<LedgerReservationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _tabaPay.Verify(t => t.ProcessPayment(payment), Times.Once);
    }

    [Fact]
    public async Task ResumeFromAsync_WhenStageIsACCOUNTLOOKUP_RunsAllStages()
    {
        var payment = TestDataBuilder.AnEvolvePaymentAtStage(
            stage: RequestStage.ACCOUNTLOOKUP, status: RequestStatus.FAILED);

        await _sut.ResumeFromAsync(payment);

        _partnerLedger.Verify(p => p.PerformAccountLookupUpdate(payment), Times.Once);
        _limitService.Verify(l => l.CheckAsync(It.IsAny<LimitCheckRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _ledgerService.Verify(l => l.ReserveAsync(It.IsAny<LedgerReservationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _tabaPay.Verify(t => t.ProcessPayment(payment), Times.Once);
    }

    [Fact]
    public async Task ResumeFromAsync_WhenAlreadyCompleted_DoesNothing()
    {
        var payment = TestDataBuilder.AnEvolvePaymentAtStage(
            stage: RequestStage.TABAPAY, status: RequestStatus.COMPLETED);

        var result = await _sut.ResumeFromAsync(payment);

        result.Should().BeSameAs(payment);
        _partnerLedger.VerifyNoOtherCalls();
        _limitService.VerifyNoOtherCalls();
        _ledgerService.VerifyNoOtherCalls();
        _tabaPay.VerifyNoOtherCalls();
        _serviceBus.VerifyNoOtherCalls();
    }

    // -------------------------------------------------------------------------
    // Failure handling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_WhenLimitDenied_ThrowsLimitExceededAndSkipsTabaPay()
    {
        _limitService
            .Setup(l => l.CheckAsync(It.IsAny<LimitCheckRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LimitCheckResult.Denied("over daily cap"));

        var payment = TestDataBuilder.AnEvolvePaymentAtStage(RequestStage.RTP_API);

        await FluentActions.Invoking(() => _sut.ProcessAsync(payment))
            .Should().ThrowAsync<LimitExceededException>();

        _tabaPay.Verify(t => t.ProcessPayment(It.IsAny<EvolvePaymentRequest>()), Times.Never);
        _serviceBus.Verify(s => s.SendMessageToServiceBusAsync(
            It.IsAny<ServiceBusContentModel>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenTabaPayThrows_PublishesFailureEnvelopeAndRethrows()
    {
        _tabaPay
            .Setup(t => t.ProcessPayment(It.IsAny<EvolvePaymentRequest>()))
            .ThrowsAsync(new TabaPayProcessingException("upstream 500"));

        var payment = TestDataBuilder.AnEvolvePaymentAtStage(RequestStage.RTP_API);

        await FluentActions.Invoking(() => _sut.ProcessAsync(payment))
            .Should().ThrowAsync<TabaPayProcessingException>();

        _serviceBus.Verify(s => s.SendMessageToServiceBusAsync(
                It.IsAny<ServiceBusContentModel>(),
                PaymentRequestConstants.FailureServiceBusSubject),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenLedgerReservationDenied_ThrowsAndSkipsTabaPay()
    {
        _ledgerService
            .Setup(l => l.ReserveAsync(It.IsAny<LedgerReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LedgerReservationResult.Failed("insufficient funds"));

        var payment = TestDataBuilder.AnEvolvePaymentAtStage(RequestStage.RTP_API);

        await FluentActions.Invoking(() => _sut.ProcessAsync(payment))
            .Should().ThrowAsync<LedgerReservationException>();

        _tabaPay.Verify(t => t.ProcessPayment(It.IsAny<EvolvePaymentRequest>()), Times.Never);
    }
}
