using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.Common;
using FluentValidation;

namespace Explore.Application.DTOs.EventAgendaItem.Validators;

public class UpdateEventAgendaItemDtoValidator : AbstractValidator<UpdateEventAgendaItemDto>
{
    public UpdateEventAgendaItemDtoValidator(
        IEventRepository eventRepository,
        ILocationRepository locationRepository,
        ILocationRoomRepository locationRoomRepository,
        IScheduleItemKindRepository scheduleItemKindRepository)
    {
        RuleFor(d => d)
            .Must(HasAnyGroup)
            .WithMessage("At least one agenda item update group must be provided.");

        When(d => d.Event is not null, () =>
        {
            RuleFor(d => d.Event!).SetValidator(new UpdateEventAgendaItemEventDtoValidator(eventRepository));
        });

        When(d => d.Title is not null, () =>
        {
            RuleFor(d => d.Title!).SetValidator(new UpdateEventAgendaItemTitleDtoValidator());
        });

        When(d => d.Description is not null, () =>
        {
            RuleFor(d => d.Description!).SetValidator(new UpdateEventAgendaItemDescriptionDtoValidator());
        });

        When(d => d.Schedule is not null, () =>
        {
            RuleFor(d => d.Schedule!).SetValidator(new UpdateEventAgendaItemScheduleDtoValidator());
        });

        When(d => d.Location is not null, () =>
        {
            RuleFor(d => d.Location!).SetValidator(new UpdateEventAgendaItemLocationDtoValidator(locationRepository));
        });

        When(d => d.Room is not null, () =>
        {
            RuleFor(d => d.Room!).SetValidator(new UpdateEventAgendaItemRoomDtoValidator(locationRoomRepository));
        });

        When(d => d.Kind is not null, () =>
        {
            RuleFor(d => d.Kind!).SetValidator(new UpdateEventAgendaItemKindDtoValidator(scheduleItemKindRepository));
        });

        When(d => d.SortOrder is not null, () =>
        {
            RuleFor(d => d.SortOrder!).SetValidator(new UpdateEventAgendaItemSortOrderDtoValidator());
        });
    }

    private static bool HasAnyGroup(UpdateEventAgendaItemDto dto) =>
        dto.Event is not null ||
        dto.Title is not null ||
        dto.Description is not null ||
        dto.Schedule is not null ||
        dto.Location is not null ||
        dto.Room is not null ||
        dto.Kind is not null ||
        dto.SortOrder is not null;
}

public class UpdateEventAgendaItemEventDtoValidator : AbstractValidator<UpdateEventAgendaItemEventDto>
{
    public UpdateEventAgendaItemEventDtoValidator(IEventRepository eventRepository)
    {
        RuleFor(d => d.EventId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (eventId, ct) => await eventRepository.Exists(eventId))
            .WithMessage("Event does not exist.");
    }
}

public class UpdateEventAgendaItemTitleDtoValidator : AbstractValidator<UpdateEventAgendaItemTitleDto>
{
    public UpdateEventAgendaItemTitleDtoValidator()
    {
        RuleFor(d => d.Value)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.");
    }
}

public class UpdateEventAgendaItemDescriptionDtoValidator : AbstractValidator<UpdateEventAgendaItemDescriptionDto>
{
    public UpdateEventAgendaItemDescriptionDtoValidator()
    {
        RuleFor(d => d.Value)
            .Must(value => value.HasValue)
            .WithMessage("Description must include an explicit field operation.");

        RuleFor(d => d.Value.Value)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.")
            .When(d => d.Value is { HasValue: true, Value: not null });
    }
}

public class UpdateEventAgendaItemScheduleDtoValidator : AbstractValidator<UpdateEventAgendaItemScheduleDto>
{
    public UpdateEventAgendaItemScheduleDtoValidator()
    {
        RuleFor(d => d.StartTime)
            .NotEmpty().WithMessage("{PropertyName} is required.");

        RuleFor(d => d.EndTime)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .GreaterThan(d => d.StartTime).WithMessage("EndTime must be after StartTime.");
    }
}

public class UpdateEventAgendaItemLocationDtoValidator : AbstractValidator<UpdateEventAgendaItemLocationDto>
{
    public UpdateEventAgendaItemLocationDtoValidator(ILocationRepository locationRepository)
    {
        RuleFor(d => d.Value)
            .Must(value => value.HasValue)
            .WithMessage("Location must include an explicit field operation.");

        RuleFor(d => d.Value.Value)
            .MustAsync(async (locationId, ct) => locationId is null || await locationRepository.Exists(locationId.Value))
            .WithMessage("Location does not exist.")
            .When(d => d.Value.HasValue);
    }
}

public class UpdateEventAgendaItemRoomDtoValidator : AbstractValidator<UpdateEventAgendaItemRoomDto>
{
    public UpdateEventAgendaItemRoomDtoValidator(ILocationRoomRepository locationRoomRepository)
    {
        RuleFor(d => d.Value)
            .Must(value => value.HasValue)
            .WithMessage("Room must include an explicit field operation.");

        RuleFor(d => d.Value.Value)
            .MustAsync(async (roomId, ct) => roomId is null || await locationRoomRepository.Exists(roomId.Value))
            .WithMessage("Room does not exist.")
            .When(d => d.Value.HasValue);
    }
}

public class UpdateEventAgendaItemKindDtoValidator : AbstractValidator<UpdateEventAgendaItemKindDto>
{
    public UpdateEventAgendaItemKindDtoValidator(IScheduleItemKindRepository scheduleItemKindRepository)
    {
        RuleFor(d => d.Value)
            .Must(value => value.HasValue)
            .WithMessage("Kind must include an explicit field operation.");

        RuleFor(d => d.Value.Value)
            .MustAsync(async (kindId, ct) => kindId is null || await scheduleItemKindRepository.Exists(kindId.Value))
            .WithMessage("Schedule item kind does not exist.")
            .When(d => d.Value.HasValue);
    }
}

public class UpdateEventAgendaItemSortOrderDtoValidator : AbstractValidator<UpdateEventAgendaItemSortOrderDto>
{
    public UpdateEventAgendaItemSortOrderDtoValidator()
    {
        RuleFor(d => d.Value)
            .GreaterThanOrEqualTo(0).WithMessage("{PropertyName} must be non-negative.");
    }
}
