using FluentValidation;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Validators;

public class BasicPaymentRequestValidator : AbstractValidator<BasicPaymentRequest>
{
    public BasicPaymentRequestValidator()
    {
        // Payment reference
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
                if (!double.TryParse(x, out var value) || value < 0)
                    context.AddFailure($"{x} is not a valid amount or less than 0");
            });

        // Source account
        RuleFor(m => m.SourceAccount)
            .NotNull()
            .SetValidator(new SourceAccountValidator()!)
            .When(m => string.IsNullOrWhiteSpace(m.SourceAccountId))
            .WithMessage("SourceAccount or SourceAccountId is required");

        // Destination account
        RuleFor(x => x.DestinationAccount)
            .NotNull()
            .SetValidator(new DestinationAccountValidator()!)
            .When(x => string.IsNullOrWhiteSpace(x.DestinationAccountId))
            .WithMessage("DestinationAccount or DestinationAccountId is required");

        // Soft descriptor — present optionally; when present, name + address required, phone optional
        When(s => s.SoftDescriptor is not null, () =>
        {
            RuleFor(s => s.SoftDescriptor!.Name)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .NotEmpty()
                .WithMessage("Soft Descriptor 'Name' is required and should not be empty");

            RuleFor(s => s.SoftDescriptor!.Address)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .WithMessage("Soft Descriptor 'Address' is required")
                .DependentRules(() =>
                {
                    RuleFor(s => s.SoftDescriptor!.Address!)
                        .SetValidator(new SoftDescriptorAddressValidator());
                });

            When(s => s.SoftDescriptor!.Phone is not null, () =>
            {
                RuleFor(s => s.SoftDescriptor!.Phone!.Number)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty()
                    .NotNull()
                    .WithMessage("Soft Descriptor 'Phone' is required");
            });
        });
    }
}
