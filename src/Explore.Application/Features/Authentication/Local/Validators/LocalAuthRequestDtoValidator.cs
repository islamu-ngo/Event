// ABOUTME: Validates local sign-in credentials before any Identity store access.
// ABOUTME: Applies bounded email and password rules without revealing account existence.

using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Configuration;
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
            .MinimumLength(LocalIdentityOptions.MinimumPasswordLength)
            .MaximumLength(LocalIdentityOptions.MaximumPasswordLength);
    }
}
