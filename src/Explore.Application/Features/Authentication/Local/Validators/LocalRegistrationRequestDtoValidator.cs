using Explore.Application.Features.Authentication.Local.Models;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Local.Validators;

public sealed class LocalRegistrationRequestDtoValidator : AbstractValidator<LocalRegistrationRequestDto>
{
    public LocalRegistrationRequestDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(request => request.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();

        RuleFor(request => request.Password)
            .NotEmpty()
            .MinimumLength(12)
            .MaximumLength(128);

        RuleFor(request => request.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(request => request.LastName)
            .NotEmpty()
            .MaximumLength(100);
    }
}
