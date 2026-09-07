namespace Explore.Application.DTOs.Appearance.Validators;

using Explore.Domain.Common.Localization;
using FluentValidation;

public class UpdateUserAppearancePreferencesDtoValidator : AbstractValidator<UpdateUserAppearancePreferencesDto>
{
    public UpdateUserAppearancePreferencesDtoValidator()
    {
        RuleFor(preferences => preferences.Localization)
            .NotNull().WithMessage("A localization update is required.")
            .SetValidator(new UpdateAppearanceLocalizationDtoValidator()!);
    }
}

public sealed class UpdateAppearanceLocalizationDtoValidator : AbstractValidator<UpdateAppearanceLocalizationDto>
{
    public UpdateAppearanceLocalizationDtoValidator()
    {
        RuleFor(localization => localization)
            .Must(localization => localization.Direction is not null || localization.Language is not null)
            .WithMessage("Localization must include direction or language.");

        RuleFor(localization => localization.Direction)
            .NotEmpty()
            .Must(direction => direction is "auto" or "ltr" or "rtl")
            .WithMessage("Direction must be one of: auto, ltr, rtl.")
            .When(localization => localization.Direction is not null);

        RuleFor(localization => localization.Language)
            .NotEmpty()
            .Must(code => code is not null && CultureRegistry.Contains(code))
            .WithMessage("Language must be a supported culture code registered in CultureRegistry.")
            .When(localization => localization.Language is not null);
    }
}
