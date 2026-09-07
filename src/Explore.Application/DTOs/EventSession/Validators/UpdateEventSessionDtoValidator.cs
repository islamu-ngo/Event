using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.DTOs.EventSession.Validators;

public class UpdateEventSessionDtoValidator : AbstractValidator<UpdateEventSessionDto>
{
    public UpdateEventSessionDtoValidator(
        IEventRepository eventRepository,
        ILocationRepository locationRepository,
        ILocationRoomRepository locationRoomRepository,
        IRegistrationModeRepository registrationModeRepository,
        IEventSessionKindRepository eventSessionKindRepository)
    {
        RuleFor(dto => dto.Event!)
            .SetValidator(new UpdateEventSessionEventDtoValidator(eventRepository))
            .When(dto => dto.Event is not null);

        RuleFor(dto => dto.Schedule!)
            .SetValidator(new UpdateEventSessionScheduleDtoValidator())
            .When(dto => dto.Schedule is not null);

        RuleFor(dto => dto.Location!)
            .SetValidator(new UpdateEventSessionLocationDtoValidator(locationRepository))
            .When(dto => dto.Location is not null);

        RuleFor(dto => dto.Room!)
            .SetValidator(new UpdateEventSessionRoomDtoValidator(locationRoomRepository))
            .When(dto => dto.Room is not null);

        RuleFor(dto => dto.SortOrder!)
            .SetValidator(new UpdateEventSessionSortOrderDtoValidator())
            .When(dto => dto.SortOrder is not null);

        RuleFor(dto => dto.Title!)
            .SetValidator(new UpdateEventSessionTitleDtoValidator())
            .When(dto => dto.Title is not null);

        RuleFor(dto => dto.Kind!)
            .SetValidator(new UpdateEventSessionKindDtoValidator(eventSessionKindRepository))
            .When(dto => dto.Kind is not null);

        RuleFor(dto => dto.Description!)
            .SetValidator(new UpdateEventSessionDescriptionDtoValidator())
            .When(dto => dto.Description is not null);

        RuleFor(dto => dto.Slug!)
            .SetValidator(new UpdateEventSessionSlugDtoValidator())
            .When(dto => dto.Slug is not null);

        RuleFor(dto => dto.MaxAudienceAttendees!)
            .SetValidator(new UpdateEventSessionMaxAudienceAttendeesDtoValidator())
            .When(dto => dto.MaxAudienceAttendees is not null);

        RuleFor(dto => dto.RegistrationMode!)
            .SetValidator(new UpdateEventSessionRegistrationModeDtoValidator(registrationModeRepository))
            .When(dto => dto.RegistrationMode is not null);

        RuleFor(dto => dto.IslamicAspect!)
            .SetValidator(new UpdateEventSessionIslamicAspectUpdateDtoValidator())
            .When(dto => dto.IslamicAspect is not null);

        RuleFor(dto => dto.FeaturedImage!)
            .SetValidator(new OptionalGuidGroupValidator<UpdateEventSessionFeaturedImageDto>(
                dto => dto.Value,
                "FeaturedImage group must include Value."))
            .When(dto => dto.FeaturedImage is not null);

        RuleFor(dto => dto)
            .Must(HasAnyGroup)
            .WithMessage("At least one event session update group must be provided.");
    }

    private static bool HasAnyGroup(UpdateEventSessionDto dto) =>
        dto.Event is not null ||
        dto.Schedule is not null ||
        dto.Location is not null ||
        dto.FeaturedImage is not null ||
        dto.Room is not null ||
        dto.SortOrder is not null ||
        dto.Title is not null ||
        dto.Kind is not null ||
        dto.Description is not null ||
        dto.Slug is not null ||
        dto.MaxAudienceAttendees is not null ||
         dto.RegistrationMode is not null ||
        dto.IslamicAspect is not null;
}

public class UpdateEventSessionEventDtoValidator : AbstractValidator<UpdateEventSessionEventDto>
{
    public UpdateEventSessionEventDtoValidator(IEventRepository eventRepository)
    {
        RuleFor(dto => dto.EventId)
            .NotEmpty().WithMessage("EventId is required.")
            .MustAsync(async (id, cancellationToken) => await eventRepository.Exists(id))
            .WithMessage("Event does not exist.");
    }
}

public class UpdateEventSessionScheduleDtoValidator : AbstractValidator<UpdateEventSessionScheduleDto>
{
    public UpdateEventSessionScheduleDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.StartTime.HasValue && dto.EndTime.HasValue && dto.EndTimeType.HasValue)
            .WithMessage("Schedule group must include StartTime, EndTime, and EndTimeType.");

        RuleFor(dto => dto.EndTimeType.Value)
            .IsInEnum().When(dto => dto.EndTimeType.HasValue)
            .WithMessage("Invalid session end-time type.");

        RuleFor(dto => dto)
            .Must(dto =>
            {
                if (!dto.EndTimeType.HasValue || !dto.StartTime.HasValue || !dto.EndTime.HasValue)
                    return true;

                var type = dto.EndTimeType.Value;
                var start = dto.StartTime.Value;
                var end = dto.EndTime.Value;

                if (type == SessionEndTimeType.Fixed && end is null)
                    return false;

                if (type == SessionEndTimeType.OpenEnded && end is not null)
                    return false;

                if (start is not null && end is not null && end <= start)
                    return false;

                return true;
            })
            .WithMessage("Invalid timing state. EndTime is required for Fixed, must be null for OpenEnded, and must be after StartTime if both are set.");
    }
}

public class UpdateEventSessionLocationDtoValidator : AbstractValidator<UpdateEventSessionLocationDto>
{
    public UpdateEventSessionLocationDtoValidator(ILocationRepository locationRepository)
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Location group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MustAsync(async (id, cancellationToken) => !id.HasValue || await locationRepository.Exists(id.Value))
            .When(dto => dto.Value.HasValue)
            .WithMessage("Location does not exist.");
    }
}

public class UpdateEventSessionRoomDtoValidator : AbstractValidator<UpdateEventSessionRoomDto>
{
    public UpdateEventSessionRoomDtoValidator(ILocationRoomRepository locationRoomRepository)
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Room group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MustAsync(async (id, cancellationToken) => !id.HasValue || await locationRoomRepository.Exists(id.Value))
            .When(dto => dto.Value.HasValue)
            .WithMessage("Room does not exist.");
    }
}

public class UpdateEventSessionSortOrderDtoValidator : AbstractValidator<UpdateEventSessionSortOrderDto>
{
    public UpdateEventSessionSortOrderDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be non-negative.");
    }
}

public class UpdateEventSessionTitleDtoValidator : AbstractValidator<UpdateEventSessionTitleDto>
{
    public UpdateEventSessionTitleDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Title group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MaximumLength(500)
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null)
            .WithMessage("Title must not exceed 500 characters.");
    }
}

public class UpdateEventSessionKindDtoValidator : AbstractValidator<UpdateEventSessionKindDto>
{
    public UpdateEventSessionKindDtoValidator(IEventSessionKindRepository eventSessionKindRepository)
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Kind group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MustAsync(async (id, cancellationToken) => !id.HasValue || await eventSessionKindRepository.Exists(id.Value))
            .When(dto => dto.Value.HasValue)
            .WithMessage("Event session kind does not exist.");
    }
}

public class UpdateEventSessionDescriptionDtoValidator : AbstractValidator<UpdateEventSessionDescriptionDto>
{
    public UpdateEventSessionDescriptionDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Description group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MaximumLength(500)
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null)
            .WithMessage("Description must not exceed 500 characters.");
    }
}

public class UpdateEventSessionSlugDtoValidator : AbstractValidator<UpdateEventSessionSlugDto>
{
    public UpdateEventSessionSlugDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Slug group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MaximumLength(500)
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null)
            .WithMessage("Slug must not exceed 500 characters.");
    }
}

public class UpdateEventSessionMaxAudienceAttendeesDtoValidator : AbstractValidator<UpdateEventSessionMaxAudienceAttendeesDto>
{
    public UpdateEventSessionMaxAudienceAttendeesDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("MaxAudienceAttendees group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .GreaterThan(0)
            .When(dto => dto.Value.HasValue && dto.Value.Value.HasValue)
            .WithMessage("MaxAudienceAttendees must be greater than 0.");
    }
}

public class UpdateEventSessionRegistrationModeDtoValidator : AbstractValidator<UpdateEventSessionRegistrationModeDto>
{
    public UpdateEventSessionRegistrationModeDtoValidator(IRegistrationModeRepository registrationModeRepository)
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("RegistrationMode group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MustAsync(async (id, cancellationToken) => !id.HasValue || await registrationModeRepository.Exists(id.Value))
            .When(dto => dto.Value.HasValue)
            .WithMessage("Registration mode does not exist.");
    }
}

public class UpdateEventSessionIslamicAspectUpdateDtoValidator : AbstractValidator<UpdateEventSessionIslamicAspectUpdateDto>
{
    public UpdateEventSessionIslamicAspectUpdateDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("IslamicAspect group must include Value.");

        RuleFor(dto => dto.Value.Value!.StartTimeType)
            .IsInEnum()
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null)
            .WithMessage("Invalid Islamic session start-time type.");

        RuleFor(dto => dto.Value.Value!.OffsetMinutes)
            .InclusiveBetween(
                EventSessionIslamicAspect.MinOffsetMinutes,
                EventSessionIslamicAspect.MaxOffsetMinutes)
            .When(dto => dto.Value.HasValue && dto.Value.Value?.OffsetMinutes is not null)
            .WithMessage(EventSessionIslamicAspectValidationRules.OffsetRangeMessage);

        RuleFor(dto => dto.Value.Value!.EndOffsetMinutes)
            .InclusiveBetween(
                EventSessionIslamicAspect.MinOffsetMinutes,
                EventSessionIslamicAspect.MaxOffsetMinutes)
            .When(dto => dto.Value.HasValue && dto.Value.Value?.EndOffsetMinutes is not null)
            .WithMessage(EventSessionIslamicAspectValidationRules.OffsetRangeMessage);

        RuleFor(dto => dto.Value.Value)
            .Must(aspect =>
            {
                if (aspect is null) return true;

                var endTimeType = aspect.EndReferencePrayer.HasValue || aspect.EndOffsetMinutes.HasValue
                    ? SessionEndTimeType.RelativeToPrayer
                    : SessionEndTimeType.Fixed;

                return EventSessionIslamicAspect.IsValidSchedulingState(
                    aspect.StartTimeType,
                    aspect.ReferencePrayer,
                    aspect.OffsetMinutes) &&
                EventSessionIslamicAspect.IsValidEndTimeSchedulingState(
                    endTimeType,
                    aspect.EndReferencePrayer,
                    aspect.EndOffsetMinutes);
            })
            .When(dto => dto.Value.HasValue)
            .WithMessage(EventSessionIslamicAspectValidationRules.SchedulingStateMessage);
    }
}

public class OptionalGuidGroupValidator<T> : AbstractValidator<T>
{
    public OptionalGuidGroupValidator(Func<T, Models.Common.OptionalUpdate<Guid?>> valueAccessor, string message)
    {
        RuleFor(dto => dto)
            .Must(dto => valueAccessor(dto).HasValue)
            .WithMessage(message);
    }
}
