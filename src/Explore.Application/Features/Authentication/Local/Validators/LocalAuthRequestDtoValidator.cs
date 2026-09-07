using Explore.Application.Features.Authentication.Local.Models;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Local.Validators;

public sealed class LocalAuthRequestDtoValidator : AbstractValidator<LocalAuthRequestDto>
{
    public LocalAuthRequestDtoValidator()
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
    }
}
