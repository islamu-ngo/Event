using FluentValidation;
using Explore.Application.DTOs.TenantSettings.Validators;
using Explore.Domain.ValueObjects;

namespace Explore.Application.DTOs.ManagedProviderProvisioning.Validators;

public class ManagedProviderClientProvisioningDtoValidator : AbstractValidator<ManagedProviderClientProvisioningDto>
{
    public ManagedProviderClientProvisioningDtoValidator()
    {
        RuleFor(x => x.ProviderKey)
            .NotEmpty().WithMessage("Provider key is required")
            .MaximumLength(255).WithMessage("Provider key cannot exceed 255 characters");

        RuleFor(x => x.ExternalSystem)
            .NotEmpty().WithMessage("External system is required")
            .MaximumLength(255).WithMessage("External system cannot exceed 255 characters");

        RuleFor(x => x.ExternalCustomerId)
            .NotEmpty().WithMessage("External customer ID is required")
            .MaximumLength(500).WithMessage("External customer ID cannot exceed 500 characters");

        RuleFor(x => x.TenantFullName)
            .NotEmpty().WithMessage("Tenant full name is required")
            .MaximumLength(500).WithMessage("Tenant full name cannot exceed 500 characters");

        RuleFor(x => x.TenantSlug)
            .NotEmpty().WithMessage("Tenant slug is required")
            .MaximumLength(500).WithMessage("Tenant slug cannot exceed 500 characters")
            .Matches("^[a-z0-9-]+$").WithMessage("Tenant slug must contain only lowercase letters, numbers, and hyphens");

        When(x => x.ActivateTenant, () =>
        {
            RuleFor(x => x.DirectoryOperatorIdentity)
                .NotNull()
                .WithMessage("Directory operator identity is required when managed provisioning activates a tenant.")
                .SetValidator(new TenantDirectoryOperatorIdentityInputDtoValidator(
                    TenantDirectoryOperatorIdentityCapability.Activation)!);
        });

        RuleFor(x => x.ExternalAdmin).NotNull().WithMessage("External admin identity is required");
        When(x => x.ExternalAdmin != null, () =>
        {
            RuleFor(x => x.ExternalAdmin.IdentityProvider)
                .NotEmpty().WithMessage("External admin identity provider is required")
                .MaximumLength(255).WithMessage("External admin identity provider cannot exceed 255 characters");

            RuleFor(x => x.ExternalAdmin.Subject)
                .NotEmpty().WithMessage("External admin subject is required")
                .MaximumLength(500).WithMessage("External admin subject cannot exceed 500 characters");

            RuleFor(x => x.ExternalAdmin.Email)
                .NotEmpty().WithMessage("External admin email is required")
                .EmailAddress().WithMessage("External admin email must be valid")
                .MaximumLength(255).WithMessage("External admin email cannot exceed 255 characters");

            RuleFor(x => x.ExternalAdmin.FirstName)
                .MaximumLength(255).WithMessage("External admin first name cannot exceed 255 characters");

            RuleFor(x => x.ExternalAdmin.LastName)
                .MaximumLength(255).WithMessage("External admin last name cannot exceed 255 characters");

            RuleFor(x => x.ExternalAdmin.DisplayName)
                .MaximumLength(500).WithMessage("External admin display name cannot exceed 500 characters");
        });

        When(x => x.Organizer != null, () =>
        {
            RuleFor(x => x.Organizer!.Kind)
                .IsInEnum().WithMessage("Organizer kind must be Organization or Group");

            RuleFor(x => x.Organizer!.FullName)
                .NotEmpty().WithMessage("Organizer full name is required")
                .MaximumLength(500).WithMessage("Organizer full name cannot exceed 500 characters");

            RuleFor(x => x.Organizer!.Email)
                .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Organizer!.Email))
                .WithMessage("Organizer email must be valid")
                .MaximumLength(255).WithMessage("Organizer email cannot exceed 255 characters");

            RuleFor(x => x.Organizer!.WebsiteUrl)
                .MaximumLength(500).WithMessage("Organizer website URL cannot exceed 500 characters");
        });
    }
}
