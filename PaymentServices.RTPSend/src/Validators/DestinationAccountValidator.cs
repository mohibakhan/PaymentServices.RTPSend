using FluentValidation;
using AccountTypeEnum = PaymentServices.RTPSend.Models.Domain.AccountType;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Validators;

public class DestinationAccountValidator : AbstractValidator<DestinationAccount>
{
    public DestinationAccountValidator()
    {
        RuleFor(x => x.AccountNumber)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .Custom((x, context) =>
            {
                if (!ulong.TryParse(x, out _))
                    context.AddFailure($"{x} is not a valid account number");
            });

        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Destination Account name is required");

        RuleFor(x => x.RoutingNumber)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .Custom((x, context) =>
            {
                if (!ulong.TryParse(x, out _))
                    context.AddFailure($"{x} is not a valid Routing number");
            });

        RuleFor(x => x.AccountType)
            .NotEmpty()
            .NotNull()
            .IsEnumName(typeof(AccountTypeEnum))
            .WithMessage("Invalid Destination Account type is required and can be one of the following values: S, C, A, B, L");
    }
}
