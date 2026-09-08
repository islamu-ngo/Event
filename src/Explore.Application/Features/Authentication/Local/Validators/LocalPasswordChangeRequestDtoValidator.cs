// ABOUTME: Bounds ordinary password change input for every command caller before native mutation.
// ABOUTME: Leaves native password policy and current-password verification to selected Identity authority.

using Explore.Application.Configuration;
using Explore.Application.Features.Authentication.Local.Models;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Local.Validators;

public sealed class LocalPasswordChangeRequestDtoValidator : AbstractValidator<LocalPasswordChangeRequestDto>
{
    public LocalPasswordChangeRequestDtoValidator()
    {
        RuleFor(request => request.CurrentPassword).NotEmpty().MaximumLength(LocalIdentityOptions.MaximumPasswordLength);
        RuleFor(request => request.NewPassword).NotEmpty()
            .MinimumLength(LocalIdentityOptions.MinimumPasswordLength).MaximumLength(LocalIdentityOptions.MaximumPasswordLength);
    }
}
