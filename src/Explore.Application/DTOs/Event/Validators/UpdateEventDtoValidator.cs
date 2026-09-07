using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.Common;
using Explore.Domain.Services.Scheduling;
using FluentValidation;

namespace Explore.Application.DTOs.Event.Validators;

public class UpdateEventDtoValidator : AbstractValidator<UpdateEventDto>
{
    public UpdateEventDtoValidator(
        IAudienceAgeRepository audienceAgeRepository,
        IAudienceGenderRepository audienceGenderRepository,
        IEventTypeRepository eventTypeRepository,
        IVisibilityTypeRepository visibilityTypeRepository,
        IEventFormatRepository eventFormatRepository,
        IStorageObjectRepository storageObjectRepository,
        IEventSeriesRepository eventSeriesRepository,
        IEventRegistrationPolicyRepository eventRegistrationPolicyRepository)
    {
        RuleFor(dto => dto)
            .Must(HasAnyGroup)
            .WithMessage("At least one event update group must be provided.");

        RuleFor(dto => dto.Title!)
            .SetValidator(new UpdateEventTitleDtoValidator())
            .When(dto => dto.Title is not null);

        RuleFor(dto => dto.Subtitle!)
            .SetValidator(new OptionalStringValueValidator<UpdateEventSubtitleDto>("Subtitle", 200, dto => dto.Value))
            .When(dto => dto.Subtitle is not null);

        RuleFor(dto => dto.Description!)
            .SetValidator(new OptionalStringValueValidator<UpdateEventDescriptionDto>("Description", 150, dto => dto.Value))
            .When(dto => dto.Description is not null);

        RuleFor(dto => dto.Content!)
            .SetValidator(new OptionalStringValueValidator<UpdateEventContentDto>("Content", 5000, dto => dto.Value))
            .When(dto => dto.Content is not null);

        RuleFor(dto => dto.Slug!)
            .SetValidator(new OptionalStringValueValidator<UpdateEventSlugDto>("Slug", 500, dto => dto.Value))
            .When(dto => dto.Slug is not null);

        RuleFor(dto => dto.EventType!)
            .SetValidator(new OptionalLookupValueValidator<UpdateEventEventTypeDto>("EventType", dto => dto.Value, eventTypeRepository.Exists))
            .When(dto => dto.EventType is not null);

        RuleFor(dto => dto.AudienceGender!)
            .SetValidator(new OptionalLookupValueValidator<UpdateEventAudienceGenderDto>("AudienceGender", dto => dto.Value, audienceGenderRepository.Exists))
            .When(dto => dto.AudienceGender is not null);

        RuleFor(dto => dto.AudienceAge!)
            .SetValidator(new OptionalLookupValueValidator<UpdateEventAudienceAgeDto>("AudienceAge", dto => dto.Value, audienceAgeRepository.Exists))
            .When(dto => dto.AudienceAge is not null);

        RuleFor(dto => dto.FeaturedImage!)
            .SetValidator(new OptionalGuidLookupValueValidator<UpdateEventFeaturedImageDto>("FeaturedImage", dto => dto.Value, storageObjectRepository.Exists))
            .When(dto => dto.FeaturedImage is not null);

        RuleFor(dto => dto.Visibility!)
            .SetValidator(new RequiredLookupValueValidator<UpdateEventVisibilityDto>("Visibility", dto => dto.Value, visibilityTypeRepository.Exists))
            .When(dto => dto.Visibility is not null);

        RuleFor(dto => dto.Format!)
            .SetValidator(new RequiredLookupValueValidator<UpdateEventFormatDto>("Format", dto => dto.Value, eventFormatRepository.Exists))
            .When(dto => dto.Format is not null);

        RuleFor(dto => dto.Madhab!)
            .SetValidator(new OptionalValuePresenceValidator<UpdateEventMadhabDto, int?>("Madhab", dto => dto.Value))
            .When(dto => dto.Madhab is not null);

        RuleFor(dto => dto.Timezone!)
            .SetValidator(new UpdateEventTimezoneDtoValidator())
            .When(dto => dto.Timezone is not null);

        RuleFor(dto => dto.EventTimeZone!)
            .SetValidator(new UpdateEventEventTimeZoneDtoValidator())
            .When(dto => dto.EventTimeZone is not null);

        RuleFor(dto => dto)
            .Must(HaveConsistentTimeZoneAliases)
            .WithMessage("EventTimeZone and Timezone must match when both are provided.");

        RuleFor(dto => dto.BackgroundColor!)
            .SetValidator(new OptionalStringValueValidator<UpdateEventBackgroundColorDto>("BackgroundColor", 500, dto => dto.Value))
            .When(dto => dto.BackgroundColor is not null);

        RuleFor(dto => dto.BackgroundEffect!)
            .SetValidator(new OptionalStringValueValidator<UpdateEventBackgroundEffectDto>("BackgroundEffect", 500, dto => dto.Value))
            .When(dto => dto.BackgroundEffect is not null);

        RuleFor(dto => dto.BackgroundImage!)
            .SetValidator(new OptionalGuidLookupValueValidator<UpdateEventBackgroundImageDto>("BackgroundImage", dto => dto.Value, storageObjectRepository.Exists))
            .When(dto => dto.BackgroundImage is not null);

        RuleFor(dto => dto.SourceTemplate!)
            .SetValidator(new OptionalValuePresenceValidator<UpdateEventSourceTemplateDto>("Template", dto => dto.Value))
            .When(dto => dto.SourceTemplate is not null);

        RuleFor(dto => dto.SeriesMembership!)
            .SetValidator(new OptionalGuidLookupValueValidator<UpdateEventSeriesMembershipDto>("Series", dto => dto.Value, eventSeriesRepository.Exists))
            .When(dto => dto.SeriesMembership is not null);

        RuleFor(dto => dto.SeriesOrder!)
            .SetValidator(new UpdateEventSeriesOrderDtoValidator())
            .When(dto => dto.SeriesOrder is not null);

        RuleFor(dto => dto.RegistrationPolicy!)
            .SetValidator(new OptionalLookupValueValidator<UpdateEventRegistrationPolicyDto>("RegistrationPolicy", dto => dto.Value, eventRegistrationPolicyRepository.Exists))
            .When(dto => dto.RegistrationPolicy is not null);
    }

    private static bool HasAnyGroup(UpdateEventDto dto) =>
        dto.Title is not null ||
        dto.Subtitle is not null ||
        dto.Description is not null ||
        dto.Content is not null ||
        dto.Slug is not null ||
        dto.EventType is not null ||
        dto.AudienceGender is not null ||
         dto.AudienceAge is not null ||
        dto.FeaturedImage is not null ||
        dto.Visibility is not null ||
        dto.Format is not null ||
        dto.Madhab is not null ||
        dto.Timezone is not null ||
        dto.EventTimeZone is not null ||
        dto.BackgroundColor is not null ||
        dto.BackgroundEffect is not null ||
        dto.BackgroundImage is not null ||
        dto.SourceTemplate is not null ||
        dto.SeriesMembership is not null ||
        dto.SeriesOrder is not null ||
        dto.RegistrationPolicy is not null;

    private static bool HaveConsistentTimeZoneAliases(UpdateEventDto dto)
    {
        if (dto.Timezone?.Value is not { HasValue: true } timezone ||
            dto.EventTimeZone?.Value is not { HasValue: true } eventTimezone ||
            string.IsNullOrWhiteSpace(timezone.Value) ||
            string.IsNullOrWhiteSpace(eventTimezone.Value))
        {
            return true;
        }

        try
        {
            return ScheduleTimeZoneResolver.NormalizeOrUtc(timezone.Value)
                == ScheduleTimeZoneResolver.NormalizeOrUtc(eventTimezone.Value);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}

public class UpdateEventTitleDtoValidator : AbstractValidator<UpdateEventTitleDto>
{
    public UpdateEventTitleDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");
    }
}

public class UpdateEventTimezoneDtoValidator : AbstractValidator<UpdateEventTimezoneDto>
{
    public UpdateEventTimezoneDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Timezone group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MaximumLength(500)
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null)
            .WithMessage("Timezone must not exceed 500 characters.");

        RuleFor(dto => dto.Value.Value)
            .Must(value => ScheduleTimeZoneResolver.IsValidOrBlank(value))
            .When(dto => dto.Value.HasValue)
            .WithMessage("Timezone must be a valid system timezone id.");
    }
}

public class UpdateEventEventTimeZoneDtoValidator : AbstractValidator<UpdateEventEventTimeZoneDto>
{
    public UpdateEventEventTimeZoneDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("EventTimeZone group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MaximumLength(500)
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null)
            .WithMessage("EventTimeZone must not exceed 500 characters.");

        RuleFor(dto => dto.Value.Value)
            .Must(value => ScheduleTimeZoneResolver.IsValidOrBlank(value))
            .When(dto => dto.Value.HasValue)
            .WithMessage("EventTimeZone must be a valid system timezone id.");
    }
}

public class UpdateEventSeriesOrderDtoValidator : AbstractValidator<UpdateEventSeriesOrderDto>
{
    public UpdateEventSeriesOrderDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("SeriesOrder group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .GreaterThanOrEqualTo(0)
            .When(dto => dto.Value.HasValue && dto.Value.Value.HasValue)
            .WithMessage("SeriesOrder must be non-negative.");
    }
}

internal class OptionalStringValueValidator<TDto> : AbstractValidator<TDto>
{
    public OptionalStringValueValidator(
        string groupName,
        int maximumLength,
        Func<TDto, OptionalUpdate<string?>> valueAccessor)
    {
        RuleFor(dto => dto)
            .Must(dto => valueAccessor(dto).HasValue)
            .WithMessage($"{groupName} group must include Value.");

        RuleFor(dto => valueAccessor(dto).Value)
            .MaximumLength(maximumLength)
            .When(dto => valueAccessor(dto).HasValue && valueAccessor(dto).Value is not null)
            .WithMessage($"{groupName} must not exceed {maximumLength} characters.");
    }
}

internal class OptionalValuePresenceValidator<TDto, TValue> : AbstractValidator<TDto>
{
    public OptionalValuePresenceValidator(
        string groupName,
        Func<TDto, OptionalUpdate<TValue>> valueAccessor)
    {
        RuleFor(dto => dto)
            .Must(dto => valueAccessor(dto).HasValue)
            .WithMessage($"{groupName} group must include Value.");
    }
}

internal sealed class OptionalValuePresenceValidator<TDto> : OptionalValuePresenceValidator<TDto, Guid?>
{
    public OptionalValuePresenceValidator(
        string groupName,
        Func<TDto, OptionalUpdate<Guid?>> valueAccessor)
        : base(groupName, valueAccessor)
    {
    }
}

internal class OptionalLookupValueValidator<TDto> : AbstractValidator<TDto>
{
    public OptionalLookupValueValidator(
        string groupName,
        Func<TDto, OptionalUpdate<int?>> valueAccessor,
        Func<int, Task<bool>> exists)
    {
        RuleFor(dto => dto)
            .Must(dto => valueAccessor(dto).HasValue)
            .WithMessage($"{groupName} group must include Value.");

        RuleFor(dto => valueAccessor(dto).Value!.Value)
            .MustAsync(async (id, _) => await exists(id))
            .When(dto => valueAccessor(dto).HasValue && valueAccessor(dto).Value.HasValue)
            .WithMessage($"{groupName} does not exist.");
    }
}

internal class OptionalGuidLookupValueValidator<TDto> : AbstractValidator<TDto>
{
    public OptionalGuidLookupValueValidator(
        string groupName,
        Func<TDto, OptionalUpdate<Guid?>> valueAccessor,
        Func<Guid, Task<bool>> exists)
    {
        RuleFor(dto => dto)
            .Must(dto => valueAccessor(dto).HasValue)
            .WithMessage($"{groupName} group must include Value.");

        RuleFor(dto => valueAccessor(dto).Value!.Value)
            .MustAsync(async (id, _) => await exists(id))
            .When(dto => valueAccessor(dto).HasValue && valueAccessor(dto).Value.HasValue)
            .WithMessage($"{groupName} does not exist.");
    }
}

internal class RequiredLookupValueValidator<TDto> : AbstractValidator<TDto>
{
    public RequiredLookupValueValidator(
        string groupName,
        Func<TDto, int> valueAccessor,
        Func<int, Task<bool>> exists)
    {
        RuleFor(dto => valueAccessor(dto))
            .NotEmpty().WithMessage($"{groupName} is required.")
            .MustAsync(async (id, _) => await exists(id))
            .WithMessage($"{groupName} does not exist.");
    }
}
