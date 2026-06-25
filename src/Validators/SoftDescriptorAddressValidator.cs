using FluentValidation;
using PaymentServices.RTPSend.Helpers.ISO3166;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Validators;

public class SoftDescriptorAddressValidator : AbstractValidator<Address>
{
    public SoftDescriptorAddressValidator()
    {
        RuleFor(x => x.AddressLines)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .WithMessage("Address is required");

        RuleFor(x => x.City)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .WithMessage("City is required");

        RuleFor(x => x.County)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .WithMessage("County is required")
            .Length(3).WithMessage("County code must be exactly 3 characters long");

        RuleFor(x => x.StateCode)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .WithMessage("state code is required")
            .Length(2).WithMessage("State code must be exactly 2 characters long");

        RuleFor(x => x.PostalCode)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .WithMessage("postal code is required");

        RuleFor(x => x.CountryISOCode)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty()
            .WithMessage("country code is required")
            .Length(3).WithMessage("Country code must be exactly 3 characters long")
            .MustAsync(BeAValidIso3166NumericCode)
            .WithMessage("Invalid ISO 3166-1 numeric country code");
    }

    private static async Task<bool> BeAValidIso3166NumericCode(string? code, CancellationToken token)
    {
        if (string.IsNullOrEmpty(code)) return false;
        var list = await CountryCodeHelper.GetListAsync();
        return list.Any(c => c.Code == code);
    }
}
