namespace Explore.Application.DTOs.EventAspects.Validators;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using FluentValidation;

/// <summary>
/// Validator for Islamic aspect create/update operations.
/// </summary>
public class CreateUpdateIslamicAspectDtoValidator : AbstractValidator<CreateUpdateIslamicAspectDto>
{
    private readonly IMadhabRepository _madhabRepository;
    private readonly ILanguageRepository _languageRepository;

    public CreateUpdateIslamicAspectDtoValidator(
        IMadhabRepository madhabRepository,
        ILanguageRepository languageRepository)
    {
        _madhabRepository = madhabRepository;
        _languageRepository = languageRepository;

        // Madhab validation (optional)
        RuleFor(x => x.MadhabId)
            .MustAsync(async (id, cancellation) =>
            {
                if (!id.HasValue) return true;
                return await _madhabRepository.Exists(id.Value);
            })
            .When(x => x.MadhabId.HasValue)
            .WithMessage("Madhab not found.");

        // Prayer time offset validation
        RuleFor(x => x.PrayerTimeOffset)
            .InclusiveBetween(-180, 180)
            .When(x => x.PrayerTimeOffset.HasValue)
            .WithMessage("Prayer time offset must be between -180 and 180 minutes.");

        // Prayer time offset requires reference prayer
        RuleFor(x => x.PrayerTimeOffset)
            .Null()
            .When(x => !x.ReferencePrayer.HasValue)
            .WithMessage("Prayer time offset requires a reference prayer to be set.");

        // Gender mode validation
        RuleFor(x => x.GenderMode)
            .IsInEnum()
            .WithMessage("Invalid gender segregation mode.");

        // Primary language validation (optional)
        RuleFor(x => x.PrimaryLanguageId)
            .MustAsync(async (id, cancellation) =>
            {
                if (!id.HasValue) return true;
                return await _languageRepository.Exists(id.Value);
            })
            .When(x => x.PrimaryLanguageId.HasValue)
            .WithMessage("Primary language not found.");
    }
}

public sealed class UpdateEventIslamicAspectDtoValidator : AbstractValidator<UpdateEventIslamicAspectDto>
{
    public UpdateEventIslamicAspectDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Jurisprudence is not null ||
                dto.PrayerSchedule is not null ||
                dto.Participation is not null ||
                dto.Language is not null)
            .WithMessage("At least one Islamic aspect update group must be provided.");
        RuleFor(dto => dto.Jurisprudence)
            .Must(group => group is null || group.MadhabId.HasValue)
            .WithMessage("The jurisprudence group must contain MadhabId.");
        RuleFor(dto => dto.PrayerSchedule)
            .Must(group => group is null ||
                group.ReferencePrayer.HasValue ||
                group.PrayerTimeOffset.HasValue)
            .WithMessage("The prayer schedule group must contain at least one supplied field.");
        RuleFor(dto => dto.Participation)
            .Must(group => group is null ||
                group.GenderMode.HasValue ||
                group.IncludesQuranRecitation.HasValue)
            .WithMessage("The participation group must contain at least one supplied field.");
        RuleFor(dto => dto.Language)
            .Must(group => group is null || group.PrimaryLanguageId.HasValue)
            .WithMessage("The language group must contain PrimaryLanguageId.");
    }
}
