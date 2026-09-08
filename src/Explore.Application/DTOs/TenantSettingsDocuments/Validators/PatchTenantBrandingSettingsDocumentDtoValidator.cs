namespace Explore.Application.DTOs.TenantSettingsDocuments.Validators;

using FluentValidation;

public sealed class PatchTenantBrandingSettingsDocumentDtoValidator : AbstractValidator<PatchTenantBrandingSettingsDocumentDto>
{
    public PatchTenantBrandingSettingsDocumentDtoValidator()
    {
        RuleFor(request => request.ExpectedConcurrencyStamp)
            .NotEmpty().WithMessage("{PropertyName} is required.");

        RuleFor(request => request)
            .Must(request => request.DisplayName is not null || request.Assets is not null)
            .WithMessage("At least one tenant branding mutation group must be provided.");

        When(request => request.DisplayName is not null, () =>
        {
            RuleFor(request => request.DisplayName)
                .Must(group => group?.Value.HasValue == true)
                .WithMessage("Display Name must include at least one field.");
        });

        When(request => request.Assets is not null, () =>
        {
            RuleFor(request => request.Assets)
                .Must(group => group is not null &&
                    (group.LogoUrl.HasValue || group.FaviconUrl.HasValue || group.CustomCssUrl.HasValue))
                .WithMessage("Assets must include at least one field.");
        });
    }
}

public sealed class TenantBrandingSettingsPayloadDtoValidator : AbstractValidator<TenantBrandingSettingsPayloadDto>
{
    public TenantBrandingSettingsPayloadDtoValidator()
    {
        RuleFor(payload => payload.DisplayName)
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(payload => payload.LogoUrl)
            .MaximumLength(2048).WithMessage("{PropertyName} must not exceed 2048 characters.");

        RuleFor(payload => payload.FaviconUrl)
            .MaximumLength(2048).WithMessage("{PropertyName} must not exceed 2048 characters.");

        RuleFor(payload => payload.CustomCssUrl)
            .MaximumLength(2048).WithMessage("{PropertyName} must not exceed 2048 characters.");
    }
}
