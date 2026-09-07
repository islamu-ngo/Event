using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public sealed class AuthorizationPolicyPackageSyncRequestDtoValidator : AbstractValidator<AuthorizationPolicyPackageSyncRequestDto>
{
    public AuthorizationPolicyPackageSyncRequestDtoValidator()
    {
        RuleFor(request => request.AdminUsername)
            .MaximumLength(256)
            .WithMessage("Cerbos Admin API username must be 256 characters or fewer.");

        RuleFor(request => request.AdminPassword)
            .MaximumLength(1024)
            .WithMessage("Cerbos Admin API password must be 1024 characters or fewer.");

        RuleFor(request => request)
            .Must(HasCompleteCredentialPair)
            .WithMessage("Provide both Cerbos Admin API username and password, or leave both empty to use deployment credentials.");
    }

    private static bool HasCompleteCredentialPair(AuthorizationPolicyPackageSyncRequestDto request)
    {
        var hasUsername = !string.IsNullOrWhiteSpace(request.AdminUsername);
        var hasPassword = !string.IsNullOrWhiteSpace(request.AdminPassword);
        return hasUsername == hasPassword;
    }
}
