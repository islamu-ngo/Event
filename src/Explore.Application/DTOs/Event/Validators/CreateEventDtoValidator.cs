using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSession.Validators;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using FluentValidation;

namespace Explore.Application.DTOs.Event.Validators;

public class CreateEventDtoValidator : AbstractValidator<CreateEventDto>
{
    public CreateEventDtoValidator(
        IAudienceAgeRepository audienceAgeRepository,
        IAudienceGenderRepository audienceGenderRepository,
        IEventTypeRepository eventTypeRepository,
        IOrganizationRepository organizationRepository,
        IGroupRepository groupRepository,
        IStorageObjectRepository storageObjectRepository,
        IEventTemplateRepository eventTemplateRepository,
        IEventSeriesRepository eventSeriesRepository,
        IEventRegistrationPolicyRepository eventRegistrationPolicyRepository,
        ILocationRepository locationRepository,
        IRegistrationModeRepository registrationModeRepository,
        ILanguageRepository languageRepository,
        IMadhabRepository madhabRepository,
        ICategoryRepository categoryRepository,
        ITagRepository tagRepository,
        IScheduleItemKindRepository scheduleItemKindRepository,
        IEventSessionKindRepository eventSessionKindRepository,
        ILocationRoomRepository locationRoomRepository,
        IEventSessionTemplateRepository eventSessionTemplateRepository,
        IActorRepository actorRepository)
    {
        RuleFor(p => p.Title)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(p => p.Subtitle).MaximumLength(200);
        RuleFor(p => p.Description).MaximumLength(150).When(p => !string.IsNullOrWhiteSpace(p.Description));
        RuleFor(p => p.Content).MaximumLength(5000).When(p => !string.IsNullOrWhiteSpace(p.Content));
        RuleFor(p => p.Slug).MaximumLength(500).When(p => !string.IsNullOrWhiteSpace(p.Slug));
        RuleFor(p => p.ParticipationConfiguration)
            .NotNull().WithMessage("ParticipationConfiguration is required.")
            .SetValidator(new ConfigureEventParticipationDtoValidator());
        RuleFor(p => p.Timezone).MaximumLength(100).When(p => !string.IsNullOrWhiteSpace(p.Timezone));
        RuleFor(p => p.EventTimeZoneId).MaximumLength(100).When(p => !string.IsNullOrWhiteSpace(p.EventTimeZoneId));
        RuleFor(p => p)
            .Must(HaveValidScheduleTimeZone)
            .WithMessage("EventTimeZoneId/Timezone must be a valid system timezone id.");
        RuleFor(p => p)
            .Must(HaveConsistentTimeZoneAliases)
            .WithMessage("EventTimeZoneId and Timezone must match when both are provided.");
        RuleFor(p => p.BackgroundColor).MaximumLength(32).When(p => !string.IsNullOrWhiteSpace(p.BackgroundColor));
        RuleFor(p => p.BackgroundEffect).MaximumLength(64).When(p => !string.IsNullOrWhiteSpace(p.BackgroundEffect));
        RuleFor(p => p.SeriesOrder).GreaterThanOrEqualTo(0).When(p => p.SeriesOrder.HasValue);

        RuleFor(p => p.IslamicAspect!.PrayerTimeOffset)
            .InclusiveBetween(-180, 180)
            .When(p => p.IslamicAspect?.PrayerTimeOffset is not null)
            .WithMessage("Prayer time offset must be between -180 and 180 minutes.");

        RuleFor(p => p.IslamicAspect!.PrayerTimeOffset)
            .Null()
            .When(p => p.IslamicAspect is not null && !p.IslamicAspect.ReferencePrayer.HasValue)
            .WithMessage("Prayer time offset requires a reference prayer to be set.");

        RuleFor(p => p.IslamicAspect!.GenderMode)
            .IsInEnum()
            .When(p => p.IslamicAspect is not null)
            .WithMessage("Invalid gender segregation mode.");

        RuleFor(p => p.EventTypeId)
            .MustAsync(async (id, _) => !id.HasValue || await eventTypeRepository.Exists(id.Value))
            .WithMessage("Event type does not exist.");

        RuleFor(p => p.AudienceGenderId)
            .MustAsync(async (id, _) => !id.HasValue || await audienceGenderRepository.Exists(id.Value))
            .WithMessage("Audience gender does not exist.");

        RuleFor(p => p.AudienceAgeId)
            .MustAsync(async (id, _) => !id.HasValue || await audienceAgeRepository.Exists(id.Value))
            .WithMessage("Audience age does not exist.");

        RuleFor(p => p.OrganizationId)
            .MustAsync(async (id, _) => !id.HasValue || await organizationRepository.Exists(id.Value))
            .WithMessage("Organization does not exist.");

        RuleFor(p => p.GroupId)
            .MustAsync(async (id, _) => !id.HasValue || await groupRepository.Exists(id.Value))
            .WithMessage("Group does not exist.");

        RuleFor(p => p)
            .Must(p => !(p.OrganizationId.HasValue && p.GroupId.HasValue))
            .WithMessage("OrganizationId and GroupId cannot both be provided.");

        RuleFor(p => p.FeaturedImageId)
            .MustAsync(async (id, _) => !id.HasValue || await storageObjectRepository.Exists(id.Value))
            .WithMessage("Featured image does not exist.");

        RuleFor(p => p.BackgroundImageId)
            .MustAsync(async (id, _) => !id.HasValue || await storageObjectRepository.Exists(id.Value))
            .WithMessage("Background image does not exist.");

        RuleFor(p => p.TemplateId)
            .MustAsync(async (id, _) => !id.HasValue || await eventTemplateRepository.Exists(id.Value))
            .WithMessage("Event template does not exist.");

        RuleFor(p => p.EventSeriesId)
            .MustAsync(async (id, _) => !id.HasValue || await eventSeriesRepository.Exists(id.Value))
            .WithMessage("Event series does not exist.");


        RuleFor(p => p.RegistrationPolicyId)
            .MustAsync(async (id, _) => !id.HasValue || await eventRegistrationPolicyRepository.Exists(id.Value))
            .WithMessage("Registration policy does not exist.");

        RuleForEach(p => p.Sessions).ChildRules(session =>
        {
            session.RuleFor(s => s.Title).MaximumLength(500).When(s => !string.IsNullOrWhiteSpace(s.Title));
            session.RuleFor(s => s.Description).MaximumLength(5000).When(s => !string.IsNullOrWhiteSpace(s.Description));
            session.RuleFor(s => s.Slug).MaximumLength(500).When(s => !string.IsNullOrWhiteSpace(s.Slug));
            session.RuleFor(s => s.TempKey).MaximumLength(80).When(s => !string.IsNullOrWhiteSpace(s.TempKey));
            session.RuleFor(s => s.DayTempKey).MaximumLength(80).When(s => !string.IsNullOrWhiteSpace(s.DayTempKey));
            session.RuleFor(s => s.RoomTempKey).MaximumLength(80).When(s => !string.IsNullOrWhiteSpace(s.RoomTempKey));
            session.RuleFor(s => s.LocationTempKey).MaximumLength(80).When(s => !string.IsNullOrWhiteSpace(s.LocationTempKey));
            session.RuleFor(s => s.StartTime).NotEmpty().WithMessage("Session start time is required.");
            session.RuleFor(s => s.EndTimeType).IsInEnum().WithMessage("Invalid session end-time type.");
            session.RuleFor(s => s.EndTime)
                .NotEmpty().When(s => s.EndTimeType == SessionEndTimeType.Fixed)
                .WithMessage("Session end time is required when EndTimeType is Fixed.");
            session.RuleFor(s => s.EndTime)
                .Empty().When(s => s.EndTimeType == SessionEndTimeType.OpenEnded)
                .WithMessage("Session end time must be empty when EndTimeType is OpenEnded.");
            session.RuleFor(s => s.EndTime)
                .GreaterThan(s => s.StartTime)
                .When(s => s.EndTime.HasValue)
                .WithMessage("Session end time must be after start time.");
            session.RuleFor(s => s.MaxAudienceAttendees).GreaterThan(0).When(s => s.MaxAudienceAttendees.HasValue);
            session.RuleFor(s => s.FeaturedImageId)
                .MustAsync(async (id, _) => !id.HasValue || await storageObjectRepository.Exists(id.Value))
                .WithMessage("Session featured image does not exist.");
        });

        RuleForEach(p => p.Locations).ChildRules(location =>
        {
            location.RuleFor(l => l.TempKey).NotEmpty().MaximumLength(80);
            location.RuleFor(l => l.FullName).NotEmpty().MaximumLength(500);
            location.RuleFor(l => l.Address).NotEmpty().MaximumLength(500);
            location.RuleFor(l => l.Postcode).NotEmpty().MaximumLength(500);
            location.RuleFor(l => l.Country).NotEmpty().MaximumLength(500);
            location.RuleFor(l => l.City).NotEmpty().MaximumLength(500);
            location.RuleFor(l => l.Timezone).MaximumLength(500).When(l => !string.IsNullOrWhiteSpace(l.Timezone));
        });

        RuleForEach(p => p.Days).ChildRules(day =>
        {
            day.RuleFor(d => d.Label).MaximumLength(200).When(d => !string.IsNullOrWhiteSpace(d.Label));
            day.RuleFor(d => d.Description).MaximumLength(2000).When(d => !string.IsNullOrWhiteSpace(d.Description));
            day.RuleFor(d => d.BannerText).MaximumLength(500).When(d => !string.IsNullOrWhiteSpace(d.BannerText));
            day.RuleFor(d => d.BannerImageId)
                .MustAsync(async (id, _) => !id.HasValue || await storageObjectRepository.Exists(id.Value))
                .WithMessage("Day banner image does not exist.");
        });

        RuleForEach(p => p.Rooms).ChildRules(room =>
        {
            room.RuleFor(r => r.TempKey).NotEmpty().MaximumLength(80);
            room.RuleFor(r => r.LocationTempKey).MaximumLength(80).When(r => !string.IsNullOrWhiteSpace(r.LocationTempKey));
            room.RuleFor(r => r)
                .Must(r => r.LocationId.HasValue ^ !string.IsNullOrWhiteSpace(r.LocationTempKey))
                .WithMessage("Room must reference exactly one existing location ID or location temp key.");
            room.RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
            room.RuleFor(r => r.Slug).MaximumLength(500).When(r => !string.IsNullOrWhiteSpace(r.Slug));
            room.RuleFor(r => r.Description).MaximumLength(2000).When(r => !string.IsNullOrWhiteSpace(r.Description));
            room.RuleFor(r => r.Capacity).GreaterThan(0).When(r => r.Capacity.HasValue);
        });

        RuleForEach(p => p.AgendaItems).ChildRules(item =>
        {
            item.RuleFor(i => i.Title).NotEmpty().MaximumLength(300);
            item.RuleFor(i => i.Description).MaximumLength(2000).When(i => !string.IsNullOrWhiteSpace(i.Description));
            item.RuleFor(i => i.TempKey).MaximumLength(80).When(i => !string.IsNullOrWhiteSpace(i.TempKey));
            item.RuleFor(i => i.DayTempKey).MaximumLength(80).When(i => !string.IsNullOrWhiteSpace(i.DayTempKey));
            item.RuleFor(i => i.RoomTempKey).MaximumLength(80).When(i => !string.IsNullOrWhiteSpace(i.RoomTempKey));
            item.RuleFor(i => i.LocationTempKey).MaximumLength(80).When(i => !string.IsNullOrWhiteSpace(i.LocationTempKey));
            item.RuleFor(i => i.StartTime).NotEmpty().WithMessage("Agenda item start time is required.");
            item.RuleFor(i => i.EndTime).NotEmpty().GreaterThan(i => i.StartTime).WithMessage("Agenda item end time must be after start time.");
            item.RuleFor(i => i.SortOrder).GreaterThanOrEqualTo(0);
        });

        RuleFor(p => p).Must(HaveUniqueTempKeys).WithMessage("Temp keys must be unique within locations, days, rooms, and sessions.");
        RuleFor(p => p).Must(HaveValidTempReferences).WithMessage("One or more location, day, or room temp-key references are invalid.");
        RuleFor(p => p).Must(HaveValidIslamicSessionScheduling).WithMessage(EventSessionIslamicAspectValidationRules.SchedulingStateMessage);

        RuleFor(p => p).MustAsync(async (request, _) => await AllLocationsExistAsync(request, locationRepository))
            .WithMessage("One or more locations do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllRegistrationModesExistAsync(request, registrationModeRepository))
            .WithMessage("One or more session registration modes do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllLanguagesExistAsync(request, languageRepository))
            .WithMessage("One or more session languages do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllEventIslamicAspectLookupsExistAsync(request, madhabRepository, languageRepository))
            .WithMessage("One or more Islamic aspect lookup references do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllCategoriesExistAsync(request, categoryRepository))
            .WithMessage("One or more categories do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllTagsExistAsync(request, tagRepository))
            .WithMessage("One or more tags do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllAgendaKindsExistAsync(request, scheduleItemKindRepository))
            .WithMessage("One or more agenda item kinds do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllSessionKindsExistAsync(request, eventSessionKindRepository))
            .WithMessage("One or more session kinds do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllExistingRoomsExistAsync(request, locationRoomRepository))
            .WithMessage("One or more rooms do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllExistingRoomsMatchLocationsAsync(request, locationRoomRepository))
            .WithMessage("One or more rooms do not belong to the submitted location.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllSessionTemplatesExistAsync(request, eventSessionTemplateRepository))
            .WithMessage("One or more session templates do not exist.");

        RuleFor(p => p).MustAsync(async (request, _) => await AllSpeakerActorsExistAsync(request, actorRepository))
            .WithMessage("One or more speaker actors do not exist.");
    }

    private static bool HaveUniqueTempKeys(CreateEventDto request)
    {
        return IsUnique(request.Locations.Select(l => l.TempKey))
            && IsUnique(request.Days.Select(d => d.TempKey))
            && IsUnique(request.Rooms.Select(r => r.TempKey))
            && IsUnique(request.Sessions.Select(s => s.TempKey));
    }

    private static bool HaveValidScheduleTimeZone(CreateEventDto request)
    {
        return ScheduleTimeZoneResolver.IsValidOrBlank(request.EventTimeZoneId ?? request.Timezone);
    }

    private static bool HaveConsistentTimeZoneAliases(CreateEventDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EventTimeZoneId) || string.IsNullOrWhiteSpace(request.Timezone))
        {
            return true;
        }

        try
        {
            return ScheduleTimeZoneResolver.NormalizeOrUtc(request.EventTimeZoneId)
                == ScheduleTimeZoneResolver.NormalizeOrUtc(request.Timezone);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool IsUnique(IEnumerable<string?> keys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()))
        {
            if (!seen.Add(key)) return false;
        }

        return true;
    }

    private static bool HaveValidTempReferences(CreateEventDto request)
    {
        var dayKeys = request.Days.Where(d => !string.IsNullOrWhiteSpace(d.TempKey)).Select(d => d.TempKey!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locationKeys = request.Locations.Where(l => !string.IsNullOrWhiteSpace(l.TempKey)).Select(l => l.TempKey.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roomKeys = request.Rooms.Select(r => r.TempKey.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return request.Rooms.All(r => IsBlankOrContained(r.LocationTempKey, locationKeys))
            && request.Sessions.All(s => IsBlankOrContained(s.DayTempKey, dayKeys) && IsBlankOrContained(s.RoomTempKey, roomKeys) && IsBlankOrContained(s.LocationTempKey, locationKeys))
            && request.AgendaItems.All(i => IsBlankOrContained(i.DayTempKey, dayKeys) && IsBlankOrContained(i.RoomTempKey, roomKeys) && IsBlankOrContained(i.LocationTempKey, locationKeys));
    }

    private static bool IsBlankOrContained(string? value, HashSet<string> allowed) =>
        string.IsNullOrWhiteSpace(value) || allowed.Contains(value.Trim());

    private static bool HaveValidIslamicSessionScheduling(CreateEventDto request)
    {
        return request.Sessions.All(session =>
            EventSessionIslamicAspectValidationRules.HasValidSchedulingState(
                session.IslamicAspect,
                session.LocationId.HasValue || !string.IsNullOrWhiteSpace(session.LocationTempKey)
                    ? Guid.Empty
                    : null));
    }

    private static async Task<bool> AllLocationsExistAsync(CreateEventDto request, ILocationRepository repository)
    {
        var ids = request.Sessions.Select(s => s.LocationId)
            .Concat(request.Rooms.Select(r => r.LocationId))
            .Concat(request.AgendaItems.Select(i => i.LocationId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct();

        foreach (var id in ids)
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static async Task<bool> AllEventIslamicAspectLookupsExistAsync(
        CreateEventDto request,
        IMadhabRepository madhabRepository,
        ILanguageRepository languageRepository)
    {
        if (request.IslamicAspect is not { } aspect)
        {
            return true;
        }

        if (aspect.MadhabId.HasValue && !await madhabRepository.Exists(aspect.MadhabId.Value))
        {
            return false;
        }

        if (aspect.PrimaryLanguageId.HasValue && !await languageRepository.Exists(aspect.PrimaryLanguageId.Value))
        {
            return false;
        }

        return true;
    }

    private static async Task<bool> AllRegistrationModesExistAsync(CreateEventDto request, IRegistrationModeRepository repository)
    {
        foreach (var id in request.Sessions.Select(s => s.RegistrationModeId).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static async Task<bool> AllSessionKindsExistAsync(CreateEventDto request, IEventSessionKindRepository repository)
    {
        foreach (var id in request.Sessions.Select(s => s.EventSessionKindId).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static async Task<bool> AllLanguagesExistAsync(CreateEventDto request, ILanguageRepository repository)
    {
        foreach (var id in request.Sessions.SelectMany(s => s.LanguageIds).Distinct())
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static async Task<bool> AllSpeakerActorsExistAsync(CreateEventDto request, IActorRepository repository)
    {
        foreach (var id in request.Sessions.SelectMany(s => s.SpeakerActorIds).Distinct())
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static async Task<bool> AllCategoriesExistAsync(CreateEventDto request, ICategoryRepository repository)
    {
        foreach (var id in request.CategoryIds.Distinct())
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static async Task<bool> AllTagsExistAsync(CreateEventDto request, ITagRepository repository)
    {
        foreach (var id in request.TagIds.Distinct())
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static async Task<bool> AllAgendaKindsExistAsync(CreateEventDto request, IScheduleItemKindRepository repository)
    {
        foreach (var id in request.AgendaItems.Select(i => i.KindId).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static async Task<bool> AllExistingRoomsExistAsync(CreateEventDto request, ILocationRoomRepository repository)
    {
        foreach (var id in GetExistingRoomIds(request))
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static async Task<bool> AllExistingRoomsMatchLocationsAsync(CreateEventDto request, ILocationRoomRepository repository)
    {
        foreach (var (roomId, locationId) in GetExistingRoomLocationPairs(request))
        {
            var room = await repository.GetById(roomId);
            if (room is null || room.LocationId != locationId) return false;
        }

        return true;
    }

    private static async Task<bool> AllSessionTemplatesExistAsync(CreateEventDto request, IEventSessionTemplateRepository repository)
    {
        foreach (var id in request.Sessions.Select(s => s.SessionTemplateId).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
        {
            if (!await repository.Exists(id)) return false;
        }

        return true;
    }

    private static IEnumerable<Guid> GetExistingRoomIds(CreateEventDto request) =>
        request.Sessions.Select(s => s.RoomId)
            .Concat(request.AgendaItems.Select(i => i.RoomId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct();

    private static IEnumerable<(Guid RoomId, Guid LocationId)> GetExistingRoomLocationPairs(CreateEventDto request)
    {
        foreach (var session in request.Sessions.Where(s => s.RoomId.HasValue && s.LocationId.HasValue))
        {
            yield return (session.RoomId!.Value, session.LocationId!.Value);
        }

        foreach (var agendaItem in request.AgendaItems.Where(i => i.RoomId.HasValue && i.LocationId.HasValue))
        {
            yield return (agendaItem.RoomId!.Value, agendaItem.LocationId!.Value);
        }
    }

}
