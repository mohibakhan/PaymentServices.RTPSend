using FluentValidation.TestHelper;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.UnitTests.TestHelpers;
using PaymentServices.RTPSend.Validators;

namespace PaymentServices.RTPSend.UnitTests.Validators;

public class BasicPaymentRequestValidatorTests
{
    private readonly BasicPaymentRequestValidator _sut = new();

    // -------------------------------------------------------------------------
    // PaymentReference
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_WhenPaymentReferenceIsNull_HasError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.PaymentReference = null!;
        _sut.TestValidate(req)
            .ShouldHaveValidationErrorFor(r => r.PaymentReference);
    }

    [Fact]
    public void Validate_WhenPaymentReferenceIsEmpty_HasError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.PaymentReference = string.Empty;
        _sut.TestValidate(req)
            .ShouldHaveValidationErrorFor(r => r.PaymentReference);
    }

    [Fact]
    public void Validate_WhenPaymentReferenceIsValid_NoError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        _sut.TestValidate(req)
            .ShouldNotHaveValidationErrorFor(r => r.PaymentReference);
    }

    // -------------------------------------------------------------------------
    // Amount
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("abc")]                  // not numeric
    [InlineData("-1.00")]                // negative
    [InlineData("12345678901234567890")] // > 18 chars
    public void Validate_WhenAmountIsInvalid_HasError(string amount)
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.Amount = amount;
        _sut.TestValidate(req)
            .ShouldHaveValidationErrorFor(r => r.Amount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.01")]
    [InlineData("100.00")]
    [InlineData("99999999.99")]
    public void Validate_WhenAmountIsValid_NoError(string amount)
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.Amount = amount;
        _sut.TestValidate(req)
            .ShouldNotHaveValidationErrorFor(r => r.Amount);
    }

    // -------------------------------------------------------------------------
    // SourceAccount / SourceAccountId — either-or
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_WhenSourceAccountIdProvidedAndSourceAccountNull_NoError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.SourceAccountId = "existing-account-id";
        req.SourceAccount = null;
        _sut.TestValidate(req)
            .ShouldNotHaveValidationErrorFor(r => r.SourceAccount);
    }

    [Fact]
    public void Validate_WhenBothSourceAccountIdAndSourceAccountAreMissing_HasError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.SourceAccountId = null;
        req.SourceAccount = null;
        _sut.TestValidate(req)
            .ShouldHaveValidationErrorFor(r => r.SourceAccount);
    }

    // -------------------------------------------------------------------------
    // SoftDescriptor — optional block, but Name required when block is present
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_WhenSoftDescriptorIsNull_NoError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.SoftDescriptor = null;
        _sut.TestValidate(req)
            .ShouldNotHaveValidationErrorFor("SoftDescriptor.Name");
    }

    [Fact]
    public void Validate_WhenSoftDescriptorSuppliedWithoutName_HasError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.SoftDescriptor = new SoftDescriptor { Name = null };
        _sut.TestValidate(req)
            .ShouldHaveValidationErrorFor("SoftDescriptor.Name");
    }

    [Fact]
    public void Validate_WhenSoftDescriptorHasNameButNullAddress_NoError()
    {
        // The fix we applied: address is optional within SoftDescriptor.
        var req = TestDataBuilder.AValidBasicRequest();
        req.SoftDescriptor = new SoftDescriptor
        {
            Name = "Test Merchant",
            Email = null,
            Phone = null,
            Address = null
        };
        _sut.TestValidate(req)
            .ShouldNotHaveValidationErrorFor("SoftDescriptor.Address");
    }

    [Fact]
    public void Validate_WhenSoftDescriptorPhoneSuppliedWithoutNumber_HasError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.SoftDescriptor = new SoftDescriptor
        {
            Name = "Test Merchant",
            Phone = new Phone { Number = null }
        };
        _sut.TestValidate(req)
            .ShouldHaveValidationErrorFor("SoftDescriptor.Phone.Number");
    }

    // -------------------------------------------------------------------------
    // RemittanceInformation — added in issue #3
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_WhenRemittanceInformationIsNull_NoError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.RemittanceInformation = null;
        _sut.TestValidate(req)
            .ShouldNotHaveValidationErrorFor(r => r.RemittanceInformation);
    }

    [Fact]
    public void Validate_WhenRemittanceInformationIsWithinLimit_NoError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.RemittanceInformation = new string('a', 140);
        _sut.TestValidate(req)
            .ShouldNotHaveValidationErrorFor(r => r.RemittanceInformation);
    }

    [Fact]
    public void Validate_WhenRemittanceInformationExceeds140Chars_HasError()
    {
        var req = TestDataBuilder.AValidBasicRequest();
        req.RemittanceInformation = new string('a', 141);
        _sut.TestValidate(req)
            .ShouldHaveValidationErrorFor(r => r.RemittanceInformation);
    }
}
