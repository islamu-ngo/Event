namespace Explore.Application.DTOs.Appearance.Validators;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using FluentValidation;

public class UpdateUiThemeDtoValidator : AbstractValidator<UpdateUiThemeDto>
{
    public UpdateUiThemeDtoValidator(IUiThemeRepository uiThemeRepository, UiTheme currentTheme)
    {
        RuleFor(theme => theme.RowVersion)
            .GreaterThan(0u).WithMessage("Row version is required.");

        RuleFor(theme => theme)
            .Must(theme => theme.Metadata is not null || theme.State is not null || theme.Palettes is not null)
            .WithMessage("At least one UI theme update group is required.")
            .Must(theme => !(theme.State?.IsDefault ?? currentTheme.IsDefault)
                || (theme.State?.IsActive ?? currentTheme.IsActive))
            .WithMessage("A default theme must remain active.");

        When(theme => theme.Metadata is not null, () =>
        {
            RuleFor(theme => theme.Metadata!)
                .Must(metadata => metadata.ThemeKey is not null || metadata.DisplayName is not null || metadata.Description.HasValue)
                .WithMessage("Metadata must include at least one update.");

            RuleFor(theme => theme.Metadata!.ThemeKey)
                .NotEmpty()
                .MaximumLength(128)
                .Must(UiThemeInputRules.IsValidThemeKey)
                .MustAsync(async (key, cancellationToken) =>
                    !await uiThemeRepository.ThemeKeyExistsAsync(
                        currentTheme.TenantId,
                        UiThemeInputRules.NormalizeThemeKey(key!),
                        currentTheme.Id))
                .WithMessage("Theme key is invalid or already exists for this catalog.")
                .When(theme => theme.Metadata!.ThemeKey is not null);

            RuleFor(theme => theme.Metadata!.DisplayName)
                .NotEmpty()
                .MaximumLength(200)
                .When(theme => theme.Metadata!.DisplayName is not null);

            RuleFor(theme => theme.Metadata!.Description.Value)
                .MaximumLength(1000)
                .When(theme => theme.Metadata!.Description.HasValue
                    && theme.Metadata.Description.Value is not null);
        });

        When(theme => theme.State is not null, () =>
        {
            RuleFor(theme => theme.State!)
                .Must(state => state.IsActive.HasValue || state.IsDefault.HasValue || state.SortOrder.HasValue)
                .WithMessage("State must include at least one update.");

            RuleFor(theme => theme.State!.SortOrder)
                .GreaterThanOrEqualTo(0)
                .When(theme => theme.State!.SortOrder.HasValue);
        });

        When(theme => theme.Palettes is not null, () =>
        {
            RuleFor(theme => theme.Palettes!)
                .Must(palettes => palettes.Light is not null || palettes.Dark is not null)
                .WithMessage("Palettes must include a light or dark palette.");

            RuleFor(theme => theme.Palettes!.Light!)
                .SetValidator(new UiThemePaletteDtoValidator())
                .When(theme => theme.Palettes!.Light is not null);

            RuleFor(theme => theme.Palettes!.Dark!)
                .SetValidator(new UiThemePaletteDtoValidator())
                .When(theme => theme.Palettes!.Dark is not null);
        });
    }
}
