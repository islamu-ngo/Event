using System;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant;
using FluentValidation;

namespace Explore.Application.DTOs.Tenant.Validators;

public class UpdateTenantDtoValidator : AbstractValidator<UpdateTenantDto>
{
    public UpdateTenantDtoValidator()
    {
        RuleFor(dto => dto.FullName!)
            .SetValidator(new UpdateTenantFullNameDtoValidator())
            .When(dto => dto.FullName is not null);

        RuleFor(dto => dto.Slug!)
            .SetValidator(new UpdateTenantSlugDtoValidator())
            .When(dto => dto.Slug is not null);

        RuleFor(dto => dto)
            .Must(dto => dto.FullName is not null || dto.Slug is not null)
            .WithMessage("At least one tenant update group must be provided.");
    }
}

public class UpdateTenantFullNameDtoValidator : AbstractValidator<UpdateTenantFullNameDto>
{
    public UpdateTenantFullNameDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(500).WithMessage("Full name cannot exceed 500 characters");
    }
}

public class UpdateTenantSlugDtoValidator : AbstractValidator<UpdateTenantSlugDto>
{
    public UpdateTenantSlugDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Slug is required")
            .MaximumLength(500).WithMessage("Slug cannot exceed 500 characters")
            .Matches("^[a-z0-9-]+$").WithMessage("Slug must contain only lowercase letters, numbers, and hyphens");
    }
}
