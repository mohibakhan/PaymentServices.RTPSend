using FluentValidation;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Validators;

public class BasicPaymentRequestValidator : AbstractValidator<BasicPaymentRequest>
{
    public BasicPaymentRequestValidator()
    {
        // Payment Reference
        RuleFor(x => x.PaymentReference)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("PaymentReference cannot be null")
            .NotEmpty().WithMessage("PaymentReference cannot be empty");

        // Amount
        RuleFor(x => x.Amount)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .MaximumLength(18)
            .Custom((x, context) =>
            {
                if (!double.TryParse(x, out double value) || value < 0)
                {
                    context.AddFailure($"{x} is not a valid amount or less than 0");
                }
            });

        // Source Account — required if SourceAccountId not supplied
        RuleFor(m => m.SourceAccount)
            .NotNull().SetValidator(new SourceAccountValidator())
            .When(m => string.IsNullOrWhiteSpace(m.SourceAccountId))
            .WithMessage("SourceAccount or SourceAccountId is required");

        // Destination Account — required if DestinationAccountId not supplied
        RuleFor(m => m.DestinationAccount)
            .NotNull().SetValidator(new DestinationAccountValidator())
            .When(m => string.IsNullOrWhiteSpace(m.DestinationAccountId))
            .WithMessage("DestinationAccount or DestinationAccountId is required");

        // Remittance Information (optional) — must be ≤140 chars per ISO 20022 RmtInf
        RuleFor(m => m.RemittanceInformation)
            .MaximumLength(140)
            .WithMessage("RemittanceInformation must be 140 characters or less")
            .When(m => !string.IsNullOrWhiteSpace(m.RemittanceInformation));

        // SoftDescriptor block (entire block is optional)
        When(s => s.SoftDescriptor != null, () =>
        {
            RuleFor(s => s.SoftDescriptor!.Name)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .NotEmpty()
                .WithMessage("Soft Descriptor 'Name' is required when SoftDescriptor is supplied");

            // Address is now OPTIONAL within SoftDescriptor.
            // Only validate sub-fields when Address is actually supplied.
            When(s => s.SoftDescriptor!.Address != null, () =>
            {
                RuleFor(s => s.SoftDescriptor!.Address!)
                    .SetValidator(new SoftDescriptorAddressValidator());
            });

            // Phone is optional, but if supplied, Number must be present
            When(s => s.SoftDescriptor!.Phone != null, () =>
            {
                RuleFor(s => s.SoftDescriptor!.Phone!.Number)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty()
                    .NotNull()
                    .WithMessage("Soft Descriptor 'Phone.Number' is required when Phone is supplied");
            });
        });
    }
}
