using FluentValidation;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Validators;

public class AccountNameValidator : AbstractValidator<AccountName>
{
    public AccountNameValidator()
    {
        RuleFor(x => x.First)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .WithMessage("Source Account first name is required");

        RuleFor(x => x.Last)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .WithMessage("Source Account last name is required");
    }
}
