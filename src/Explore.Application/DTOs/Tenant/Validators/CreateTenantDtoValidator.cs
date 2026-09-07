using Explore.Application.DTOs.Tenant;
using Explore.Application.DTOs.TenantSettings.Validators;
using Explore.Domain.ValueObjects;
using FluentValidation;

namespace Explore.Application.DTOs.Tenant.Validators;

public class CreateTenantDtoValidator : AbstractValidator<CreateTenantDto>
{
    public CreateTenantDtoValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(500).WithMessage("Full name cannot exceed 500 characters");

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("Slug is required")
            .MaximumLength(500).WithMessage("Slug cannot exceed 500 characters")
            .Matches("^[a-z0-9-]+$").WithMessage("Slug must contain only lowercase letters, numbers, and hyphens");

        When(x => x.IsActive, () =>
        {
            RuleFor(x => x.DirectoryOperatorIdentity)
                .NotNull()
                .WithMessage("Directory operator identity is required for Active tenant creation.")
                .SetValidator(new TenantDirectoryOperatorIdentityInputDtoValidator(
                    TenantDirectoryOperatorIdentityCapability.Activation)!);
        });
    }
}
