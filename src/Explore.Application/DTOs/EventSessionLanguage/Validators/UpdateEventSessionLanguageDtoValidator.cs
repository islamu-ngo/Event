using FluentValidation;

namespace Explore.Application.DTOs.EventSessionLanguage.Validators;

public class UpdateEventSessionLanguageDtoValidator : AbstractValidator<UpdateEventSessionLanguageDto>
{
    public UpdateEventSessionLanguageDtoValidator()
    {
        RuleFor(x => x)
            .Must(HasAtLeastOneGroup)
            .WithMessage("At least one event session language update group must be provided.");

        When(x => x.Session is not null, () =>
        {
            RuleFor(x => x.Session!.EventSessionId)
                .NotEmpty().WithMessage("EventSessionId is required.");
        });

        When(x => x.Language is not null, () =>
        {
            RuleFor(x => x.Language!.LanguageId)
                .NotEmpty().WithMessage("LanguageId is required.");
        });
    }

    private static bool HasAtLeastOneGroup(UpdateEventSessionLanguageDto dto) =>
        dto.Session is not null ||
        dto.Language is not null;
}
