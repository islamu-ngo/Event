namespace Explore.Application.DTOs.Footer.Validators;

using FluentValidation;

public sealed class PatchTenantFooterSettingsDtoValidator : AbstractValidator<PatchTenantFooterSettingsDto>
{
    public PatchTenantFooterSettingsDtoValidator()
    {
        RuleFor(patch => patch)
            .Must(HasAtLeastOneLeaf)
            .WithMessage("At least one tenant footer settings field must be provided.");

        When(patch => patch.Template?.Value.HasValue == true, () =>
        {
            RuleFor(patch => patch.Template!.Value.Value)
                .NotNull().WithMessage("Template Value must not be null.");
        });

        When(patch => patch.Description?.Text.HasValue == true, () =>
        {
            RuleFor(patch => patch.Description!.Text.Value)
                .NotNull().WithMessage("Description Text must not be null.");
        });

        When(patch => patch.SocialLinks?.Items.HasValue == true, () =>
        {
            RuleFor(patch => patch.SocialLinks!.Items.Value)
                .NotNull().WithMessage("Social Links Items must not be null.");
        });

        When(patch => patch.Copyright?.Text.HasValue == true, () =>
        {
            RuleFor(patch => patch.Copyright!.Text.Value)
                .NotNull().WithMessage("Copyright Text must not be null.");
        });
    }

    private static bool HasAtLeastOneLeaf(PatchTenantFooterSettingsDto patch)
        => patch.General is { } general
               && (general.Enabled.HasValue || general.ShowCookieSettingsLink.HasValue)
           || patch.Template?.Value.HasValue == true
           || patch.Description is { } description
               && (description.Show.HasValue || description.Text.HasValue)
           || patch.SocialLinks is { } socialLinks
               && (socialLinks.Show.HasValue || socialLinks.Items.HasValue)
           || patch.Copyright?.Text.HasValue == true;
}
