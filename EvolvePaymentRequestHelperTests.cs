using Microsoft.Azure.Cosmos;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Models.Response;

namespace PaymentServices.RTPSend.UnitTests.Helpers;

public class EvolvePaymentRequestHelperTests
{
    // -------------------------------------------------------------------------
    // SetAccountLookupPatchoperation — all the fields must use Set, not Replace,
    // because they don't exist on the document when the partner-ledger stage runs.
    // -------------------------------------------------------------------------

    [Fact]
    public void SetAccountLookupPatchoperation_AllOpsAreSet()
    {
        var response = new PartnerLedgerResponse
        {
            FboAccount = "fbo-1",
            FboAccountName = "FBO Test",
            CifNo = "cif-1",
            TaxId = "tax-1",
            UserIsBusiness = "1"
        };

        var ops = EvolvePaymentRequestHelper.SetAccountLookupPatchoperation(response);

        ops.Should().AllSatisfy(op =>
            op.OperationType.Should().Be(PatchOperationType.Set,
                "fields populated by downstream stages may not exist on the doc yet"));
    }

    [Fact]
    public void SetAccountLookupPatchoperation_PatchesExpectedPaths()
    {
        var response = new PartnerLedgerResponse
        {
            FboAccount = "fbo-1",
            FboAccountName = "FBO Test",
            CifNo = "cif-1",
            TaxId = "tax-1",
            UserIsBusiness = "1"
        };

        var ops = EvolvePaymentRequestHelper.SetAccountLookupPatchoperation(response);

        var paths = ops.Select(o => o.Path).ToList();
        paths.Should().Contain(new[]
        {
            "/fboAccount",
            "/fboAccountName",
            "/fintechId",
            "/taxId",
            "/userIsBusiness"
        });
    }

    // -------------------------------------------------------------------------
    // GetTabaPaypatchoperation — same Set-not-Replace requirement
    // -------------------------------------------------------------------------

    [Fact]
    public void GetTabaPaypatchoperation_AllOpsAreSet()
    {
        var ops = EvolvePaymentRequestHelper.GetTabaPaypatchoperation(
            tabaPayTransactionId: "txn-1",
            tabaPayReferenceId: "ref-1",
            instructionId: "instr-1");

        ops.Should().AllSatisfy(op =>
            op.OperationType.Should().Be(PatchOperationType.Set));
    }

    [Fact]
    public void GetTabaPaypatchoperation_PatchesExpectedPaths()
    {
        var ops = EvolvePaymentRequestHelper.GetTabaPaypatchoperation(
            tabaPayTransactionId: "txn-1",
            tabaPayReferenceId: "ref-1",
            instructionId: "instr-1");

        ops.Select(o => o.Path).Should().BeEquivalentTo(new[]
        {
            "/tabaPayTransactionId",
            "/tabaPayReferenceId",
            "/instructionId"
        });
    }

    // -------------------------------------------------------------------------
    // GetStatusPatchOperation — stage/status are always present so use Replace.
    // Status history is appended via Add.
    // -------------------------------------------------------------------------

    [Fact]
    public void GetStatusPatchOperation_StageAndStatusUseReplace()
    {
        var ops = EvolvePaymentRequestHelper.GetStatusPatchOperation(
            RequestStage.TABAPAY, RequestStatus.COMPLETED);

        ops.Should().Contain(op =>
            op.Path == "/stage" && op.OperationType == PatchOperationType.Replace);
        ops.Should().Contain(op =>
            op.Path == "/status" && op.OperationType == PatchOperationType.Replace);
    }

    [Fact]
    public void GetStatusPatchOperation_StatusHistoryUsesAdd()
    {
        var ops = EvolvePaymentRequestHelper.GetStatusPatchOperation(
            RequestStage.TABAPAY, RequestStatus.COMPLETED);

        ops.Should().Contain(op =>
            op.Path == "/statusHistory/-" && op.OperationType == PatchOperationType.Add);
    }

    [Fact]
    public void GetStatusPatchOperation_ModifiedTimestampUsesSet()
    {
        // Defensive Set: initial doc may not have modifiedTimeStamp populated
        var ops = EvolvePaymentRequestHelper.GetStatusPatchOperation(
            RequestStage.TABAPAY, RequestStatus.COMPLETED);

        ops.Should().Contain(op =>
            op.Path == "/modifiedTimeStamp" && op.OperationType == PatchOperationType.Set);
    }
}
