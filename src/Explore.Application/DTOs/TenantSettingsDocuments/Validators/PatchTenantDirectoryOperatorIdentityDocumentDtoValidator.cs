namespace Explore.Application.DTOs.TenantSettingsDocuments.Validators;

using FluentValidation;

public sealed class PatchTenantDirectoryOperatorIdentityDocumentDtoValidator
    : AbstractValidator<PatchTenantDirectoryOperatorIdentityDocumentDto>
{
    public PatchTenantDirectoryOperatorIdentityDocumentDtoValidator()
    {
        RuleFor(patch => patch.ExpectedConcurrencyStamp)
            .NotEmpty().WithMessage("Expected concurrency stamp is required.");

        RuleFor(patch => patch)
            .Must(patch => patch.LegalEntity is not null
                || patch.Contacts is not null
                || patch.LegalLinks is not null)
            .WithMessage("At least one directory-operator identity mutation group must be provided.");

        When(patch => patch.LegalEntity is not null, () =>
        {
            RuleFor(patch => patch.LegalEntity)
                .Must(group => group is not null
                    && (group.PublicName.HasValue
                        || group.LegalName.HasValue
                        || group.OperatorKindCode.HasValue
                        || group.JurisdictionCountryCode.HasValue
                        || group.RegistrationIdentifier.HasValue))
                .WithMessage("Legal entity must include at least one field.");
        });

        When(patch => patch.Contacts is not null, () =>
        {
            RuleFor(patch => patch.Contacts)
                .Must(group => group?.PublicContactEmail.HasValue == true)
                .WithMessage("Contacts must include at least one field.");
        });

        When(patch => patch.LegalLinks is not null, () =>
        {
            RuleFor(patch => patch.LegalLinks)
                .Must(group => group is not null
                    && (group.LegalNoticeUrl.HasValue
                        || group.TermsUrl.HasValue
                        || group.PrivacyUrl.HasValue))
                .WithMessage("Legal links must include at least one field.");
        });
    }
}
