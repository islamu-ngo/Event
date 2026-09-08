namespace Explore.Application.DTOs.Appearance.Validators;

using Explore.Application.Contracts.Persistence;
using FluentValidation;

public class CreateUiThemeDtoValidator : AbstractValidator<CreateUiThemeDto>
{
    public CreateUiThemeDtoValidator(IUiThemeRepository uiThemeRepository, Guid? tenantId)
    {
        RuleFor(theme => theme.ThemeKey)
            .NotEmpty().WithMessage("Theme key is required.")
            .MaximumLength(128).WithMessage("Theme key cannot exceed 128 characters.")
            .Must(UiThemeInputRules.IsValidThemeKey).WithMessage("Theme key must use lowercase letters, numbers, hyphens, or underscores.")
            .MustAsync(async (_, key, cancellationToken) =>
                !await uiThemeRepository.ThemeKeyExistsAsync(tenantId, UiThemeInputRules.NormalizeThemeKey(key), null))
            .WithMessage("A theme with the same key already exists for this catalog.");

        RuleFor(theme => theme.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(200).WithMessage("Display name cannot exceed 200 characters.");

        RuleFor(theme => theme.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.");

        RuleFor(theme => theme.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Sort order cannot be negative.");

        RuleFor(theme => theme)
            .Must(theme => !theme.IsDefault || theme.IsActive)
            .WithMessage("A default theme must remain active.");

        RuleFor(theme => theme.LightPalette)
            .NotNull().WithMessage("Light palette is required.")
            .SetValidator(new UiThemePaletteDtoValidator());

        RuleFor(theme => theme.DarkPalette)
            .NotNull().WithMessage("Dark palette is required.")
            .SetValidator(new UiThemePaletteDtoValidator());
    }
}
