using Explore.Blazor.Client.Clients;
using FluentValidation;

namespace Explore.Blazor.Client.Validators;

public class UpdateLocationDtoValidator : AbstractValidator<UpdateLocationDto>
{
    public UpdateLocationDtoValidator()
    {
        RuleFor(x => x.FullName!.Value)
            .NotEmpty()
            .When(x => x.FullName is not null);

        RuleFor(x => x)
            .Must(x =>
                x.FullName is not null ||
                x.Address is not null ||
                x.Postcode is not null ||
                x.Country is not null ||
                x.City is not null ||
                x.Timezone is not null)
            .WithMessage("At least one location update group must be provided.");
    }
}
