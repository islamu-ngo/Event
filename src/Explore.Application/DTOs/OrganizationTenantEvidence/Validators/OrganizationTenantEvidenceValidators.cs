using FluentValidation;

namespace Explore.Application.DTOs.OrganizationTenantEvidence.Validators;

public sealed class CreateOrganizationTenantEvidenceUploadSessionDtoValidator
    : AbstractValidator<CreateOrganizationTenantEvidenceUploadSessionDto>
{
    public CreateOrganizationTenantEvidenceUploadSessionDtoValidator()
    {
        RuleFor(dto => dto.FileName).NotEmpty().MaximumLength(500);
        RuleFor(dto => dto.ContentType)
            .Equal("application/pdf", StringComparer.OrdinalIgnoreCase)
            .WithMessage("Only PDF evidence documents are supported.");
        RuleFor(dto => dto.ExpectedSizeBytes).GreaterThan(0);
    }
}

public sealed class SubmitOrganizationTenantEvidenceDtoValidator
    : AbstractValidator<SubmitOrganizationTenantEvidenceDto>
{
    public SubmitOrganizationTenantEvidenceDtoValidator()
    {
        RuleFor(dto => dto.DocumentStorageObjectId).NotEmpty();
    }
}

public sealed class ReviewOrganizationTenantEvidenceDtoValidator
    : AbstractValidator<ReviewOrganizationTenantEvidenceDto>
{
    public ReviewOrganizationTenantEvidenceDtoValidator()
    {
        RuleFor(dto => dto.Decision).IsInEnum();
        RuleFor(dto => dto.ExpectedConcurrencyStamp).NotEmpty();
        RuleFor(dto => dto.Notes).MaximumLength(2000);
    }
}
