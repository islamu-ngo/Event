using Explore.Application.Features.Geocoding.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Geocoding.Validators;

public sealed class PromoteLocationAddressCommandValidator
    : AbstractValidator<PromoteLocationAddressCommand>
{
    public PromoteLocationAddressCommandValidator()
    {
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.ExpectedConcurrencyStamp).NotEmpty();
    }
}
