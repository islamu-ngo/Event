
using Explore.Domain.Authentication;
using Explore.Application.Features.Authentication.Local.Models;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Local.Validators;

public sealed class LocalPasswordChangeRequestDtoValidator : AbstractValidator<LocalPasswordChangeRequestDto>
{
    public LocalPasswordChangeRequestDtoValidator()
    {
        RuleFor(request => request.CurrentPassword)
            .Must(password => LocalPasswordPolicy.TryNormalizeForVerification(password, out _));
        RuleFor(request => request.NewPassword)
            .Must(password => LocalPasswordPolicy.TryNormalizeNewCredential(password, out _));
    }
}
