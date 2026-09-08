namespace Explore.Application.DTOs.Appearance.Validators;

using Explore.Domain.Enums;
using FluentValidation;

public sealed class UpdateAppearanceProfileRequestDtoValidator : AbstractValidator<UpdateAppearanceProfileRequestDto>
{
    public UpdateAppearanceProfileRequestDtoValidator()
    {
        RuleFor(request => request)
            .Must(request => request.Metadata is not null || request.Palettes is not null)
            .WithMessage("At least one appearance profile update group is required.");

        When(request => request.Metadata is not null, () =>
        {
            RuleFor(request => request.Metadata!)
                .Must(metadata => metadata.Name is not null || metadata.ThemeMode is not null)
                .WithMessage("Metadata must include name or theme mode.");

            RuleFor(request => request.Metadata!.Name)
                .NotEmpty()
                .MaximumLength(200)
                .When(request => request.Metadata!.Name is not null);

            RuleFor(request => request.Metadata!.ThemeMode)
                .NotEmpty()
                .Must(mode => Enum.TryParse<AppearanceThemeMode>(mode, true, out _))
                .WithMessage("Theme mode is invalid.")
                .When(request => request.Metadata!.ThemeMode is not null);
        });

        When(request => request.Palettes is not null, () =>
        {
            RuleFor(request => request.Palettes!)
                .Must(palettes => palettes.Light is not null || palettes.Dark is not null)
                .WithMessage("Palettes must include a light or dark palette.");

            RuleFor(request => request.Palettes!.Light!)
                .SetValidator(new UiThemePaletteDtoValidator())
                .When(request => request.Palettes!.Light is not null);

            RuleFor(request => request.Palettes!.Dark!)
                .SetValidator(new UiThemePaletteDtoValidator())
                .When(request => request.Palettes!.Dark is not null);
        });
    }
}
