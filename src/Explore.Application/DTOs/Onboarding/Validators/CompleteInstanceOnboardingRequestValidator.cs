using Explore.Domain.Enums;
using Explore.Application.DTOs.TenantSettings.Validators;
using Explore.Domain.ValueObjects;
using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public class CompleteInstanceOnboardingRequestValidator : AbstractValidator<CompleteInstanceOnboardingRequest>
{
    public CompleteInstanceOnboardingRequestValidator()
    {
        RuleFor(x => x.DeploymentMode)
            .IsInEnum()
            .WithMessage("DeploymentMode must be SingleTenant or MultiTenant.");

        RuleFor(x => x.SiteProfile)
            .NotNull()
            .WithMessage("SiteProfile is required.")
            .SetValidator(new SelfHostOnboardingProfileDtoValidator());

        When(x => x.DeploymentMode == DeploymentMode.SingleTenant, () =>
        {
            RuleFor(x => x.DirectoryOperatorIdentity)
                .NotNull()
                .WithMessage("Directory operator identity is required for single-tenant onboarding.")
                .SetValidator(new TenantDirectoryOperatorIdentityInputDtoValidator(
                    TenantDirectoryOperatorIdentityCapability.Activation)!);
        });

        RuleFor(x => x.InstanceName)
            .MaximumLength(200)
            .When(x => x.InstanceName is not null)
            .WithMessage("InstanceName must not exceed 200 characters.");

        RuleFor(x => x.AdministrationAccessMode)
            .Must(BeKnownAdministrationAccessMode)
            .WithMessage("AdministrationAccessMode must be Embedded, DedicatedAdminHost, or SeparateControlPlaneApp.");

        RuleFor(x => x)
            .Must(x => x.DeploymentMode != DeploymentMode.SingleTenant || IsEmbeddedAccess(x.AdministrationAccessMode))
            .WithMessage("Single-tenant onboarding does not support platform administration access choices.");

        RuleFor(x => x.AdminHost)
            .Empty()
            .When(x => x.DeploymentMode == DeploymentMode.SingleTenant)
            .WithMessage("AdminHost is only available for multi-tenant onboarding.");

        RuleFor(x => x)
            .Must(x => !IsDedicatedAdminHostAccess(x.AdministrationAccessMode) || !string.IsNullOrWhiteSpace(x.AdminHost))
            .When(x => x.DeploymentMode == DeploymentMode.MultiTenant)
            .WithMessage("AdminHost is required when dedicated admin hostname access is selected.");

        RuleFor(x => x.AdminHost)
            .Empty()
            .When(x => x.DeploymentMode == DeploymentMode.MultiTenant && IsEmbeddedAccess(x.AdministrationAccessMode))
            .WithMessage("AdminHost must be empty when embedded administration access is selected.");

        RuleFor(x => x)
            .Must(x => !IsSeparateControlPlaneAppAccess(x.AdministrationAccessMode))
            .WithMessage("Separate control-plane app access is not available during onboarding yet.");
    }

    private static bool BeKnownAdministrationAccessMode(string? value)
        => IsEmbeddedAccess(value)
           || IsDedicatedAdminHostAccess(value)
           || IsSeparateControlPlaneAppAccess(value);

    private static bool IsEmbeddedAccess(string? value)
        => string.IsNullOrWhiteSpace(value)
           || value.Equals(CompleteInstanceOnboardingRequest.EmbeddedAdministrationAccess, StringComparison.OrdinalIgnoreCase);

    private static bool IsDedicatedAdminHostAccess(string? value)
        => value?.Equals(CompleteInstanceOnboardingRequest.DedicatedAdminHostAdministrationAccess, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsSeparateControlPlaneAppAccess(string? value)
        => value?.Equals(CompleteInstanceOnboardingRequest.SeparateControlPlaneAppAdministrationAccess, StringComparison.OrdinalIgnoreCase) == true;
}
