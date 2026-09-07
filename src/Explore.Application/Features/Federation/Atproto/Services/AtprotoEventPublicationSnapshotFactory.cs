using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Features.Federation.Atproto.Services;

public sealed partial class AtprotoEventPublicationSnapshotFactory(
    PublicEventLocationDisclosureEvaluator locationDisclosureEvaluator)
{
    public async Task<AtprotoEventPublicationSnapshotResult> CreateAsync(
        AtprotoEventPublicationEntityGraph graph,
        DateTimeOffset serverNowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Event eventEntity = graph.Event;
        List<string> errors = ValidateGraph(graph, serverNowUtc);
        if (errors.Count != 0)
        {
            return AtprotoEventPublicationSnapshotResult.Ineligible(errors);
        }

        IReadOnlyList<EventSession> sessions = graph.Sessions
            .Where(IsPublicSession)
            .OrderBy(session => session.StartTime is null)
            .ThenBy(session => session.StartTime)
            .ThenBy(session => session.SortOrder)
            .ThenBy(session => session.Title, StringComparer.Ordinal)
            .ThenBy(session => session.Id)
            .ToArray();
        Dictionary<PublicEventLocationDisclosureMemoKey, PublicEventLocationDisclosureInput> disclosureInputs =
            BuildDisclosureInputs(graph, sessions, serverNowUtc, errors);
        if (errors.Count != 0)
        {
            return AtprotoEventPublicationSnapshotResult.Ineligible(errors);
        }

        IReadOnlyDictionary<PublicEventLocationDisclosureMemoKey, EventLocationDisclosureResult> disclosures =
            await locationDisclosureEvaluator.EvaluateManyAsync(disclosureInputs.Values, cancellationToken);

        AtprotoEventLocationSnapshot? ResolveLocation(Guid? eventLocationId, Guid? legacyLocationId, Guid? roomId)
        {
            if (eventLocationId is null)
            {
                return null;
            }

            var key = new PublicEventLocationDisclosureMemoKey(eventLocationId.Value, roomId);
            return disclosures.TryGetValue(key, out EventLocationDisclosureResult? result)
                ? MapLocation(result)
                : null;
        }

        ImmutableArray<AtprotoEventSessionSnapshot> sessionSnapshots = sessions
            .Select(session => MapSession(graph, session, ResolveLocation))
            .ToImmutableArray();
        ImmutableArray<AtprotoEventLocationSnapshot> locations = disclosures.Values
            .Select(MapLocation)
            .Where(location => location is not null)
            .Cast<AtprotoEventLocationSnapshot>()
            .Distinct()
            .OrderBy(LocationSortKey, StringComparer.Ordinal)
            .ToImmutableArray();
        DateTimeOffset? startsAt = sessions.Select(session => session.StartTime).Where(value => value.HasValue).Min();
        DateTimeOffset? endsAt = sessions
            .Select(session => session.EndTime)
            .Where(value => value.HasValue)
            .Max();

        var snapshot = new AtprotoEventPublicationSnapshot(
            Normalize(eventEntity.Title)!,
            Normalize(eventEntity.Description),
            NormalizeRichText(eventEntity.Content),
            ToUtc(eventEntity.CreatedAt),
            startsAt?.ToUniversalTime(),
            endsAt?.ToUniversalTime(),
            MapMode(eventEntity.EventFormat.MasterCode),
            MapStatus(eventEntity.EventStatus.MasterCode),
            eventEntity.ParticipationConfiguration!.AdvanceRegistrationObligationId
                == (int)AdvanceRegistrationObligationEnum.Required,
            MapDetails(eventEntity),
            MapOrganizer(eventEntity.Actor),
            MapSeries(eventEntity.EventSeries, eventEntity.SeriesOrder),
            MapIslamicAspect(eventEntity.IslamicAspect),
            MapTechAspect(eventEntity.TechAspect),
            new(
                Normalize(eventEntity.BackgroundColor),
                Normalize(eventEntity.BackgroundEffect),
                PublicStorageDescription(eventEntity.FeaturedImage),
                PublicStorageDescription(eventEntity.BackgroundImage)),
            graph.Categories
                .Select(link => LookupPath(link.Category))
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            graph.Tags
                .Select(link => LookupLabel(link.Tag.MasterCode, link.Tag.FullName))
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            locations,
            graph.Days
                .Where(day => day.IsPublished)
                .OrderBy(day => day.SortOrder)
                .ThenBy(day => day.LocalDate)
                .ThenBy(day => day.Id)
                .Select(MapDay)
                .ToImmutableArray(),
            sessionSnapshots,
            graph.SessionGroups
                .Where(group => group.IsPublished)
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.Name, StringComparer.Ordinal)
                .ThenBy(group => group.Id)
                .Select(group => MapGroup(graph, group, ResolveLocation))
                .ToImmutableArray(),
            graph.AgendaItems
                .OrderBy(item => item.StartTime)
                .ThenBy(item => item.SortOrder)
                .ThenBy(item => item.Title, StringComparer.Ordinal)
                .ThenBy(item => item.Id)
                .Select(item => new AtprotoEventAgendaItemSnapshot(
                    Normalize(item.Title)!,
                    Normalize(item.Description),
                    item.StartTime.ToUniversalTime(),
                    item.EndTime.ToUniversalTime(),
                    item.LocalStartDate,
                    item.LocalEndDate,
                    item.LocalStartTime,
                    item.LocalEndTime,
                    item.Kind is null ? null : LookupLabel(item.Kind.MasterCode, item.Kind.FullName, item.Kind.Description),
                    item.SortOrder,
                    ResolveLocation(item.EventLocationId, item.LocationId, item.RoomId)))
                .ToImmutableArray(),
            graph.CustomPropertyDefinitions
                .Where(IsPublicCustomProperty)
                .OrderBy(definition => definition.SortOrder)
                .ThenBy(definition => definition.DisplayName, StringComparer.Ordinal)
                .ThenBy(definition => definition.Namespace, StringComparer.Ordinal)
                .ThenBy(definition => definition.Key, StringComparer.Ordinal)
                .ThenBy(definition => definition.Id)
                .Select(MapCustomProperty)
                .ToImmutableArray(),
            BuildUris(eventEntity));

        return AtprotoEventPublicationSnapshotResult.Eligible(snapshot);
    }

    private static List<string> ValidateGraph(
        AtprotoEventPublicationEntityGraph graph,
        DateTimeOffset serverNowUtc)
    {
        Event eventEntity = graph.Event;
        var errors = new List<string>();
        if (eventEntity.Id == Guid.Empty || eventEntity.TenantId == Guid.Empty)
        {
            errors.Add("The event and tenant identities must be persisted before federation.");
        }

        if (string.IsNullOrWhiteSpace(eventEntity.Title) || serverNowUtc == default)
        {
            errors.Add("The event requires a name and a valid server timestamp.");
        }

        if (eventEntity.IsDeleted
            || eventEntity.EventStatusId == (int)EventStatusEnum.Moderated
            || eventEntity.VisibilityTypeId != (int)VisibilityTypeEnum.Public)
        {
            errors.Add("Only active, non-moderated public events are federatable.");
        }

        if (eventEntity.EventFormat is null
            || eventEntity.EventStatus is null
            || eventEntity.VisibilityType is null
            || eventEntity.Actor?.Pii is null
            || eventEntity.ParticipationConfiguration is null)
        {
            errors.Add("Required public event lookups, participation configuration, and organizer data were not loaded.");
        }

        if (graph.Sessions.Any(session => IsPublicSession(session) && session.EventSessionStatus is null))
        {
            errors.Add("Every public session requires its status lookup before federation.");
        }

        if (HasCrossTenantOrEventRows(graph))
        {
            errors.Add("The publication graph contains a cross-tenant or cross-event row.");
        }

        return errors;
    }

    private static bool HasCrossTenantOrEventRows(AtprotoEventPublicationEntityGraph graph)
    {
        Guid tenantId = graph.Event.TenantId;
        Guid eventId = graph.Event.Id;
        HashSet<Guid> sessionIds = graph.Sessions.Select(session => session.Id).ToHashSet();
        return graph.Event.EventSeries is { } series && series.TenantId != tenantId
            || graph.EventLocations.Any(row => row.TenantId != tenantId || row.EventId != eventId)
            || graph.Sessions.Any(row => row.TenantId != tenantId || row.EventId != eventId)
            || graph.Days.Any(row => row.TenantId != tenantId || row.EventId != eventId)
            || graph.SessionGroups.Any(row => row.TenantId != tenantId || row.EventId != eventId)
            || graph.SessionGroupSessions.Any(row => row.TenantId != tenantId || row.EventId != eventId)
            || graph.AgendaItems.Any(row => row.TenantId != tenantId || row.EventId != eventId)
            || graph.SessionAgendaItems.Any(row => row.TenantId != tenantId || !sessionIds.Contains(row.EventSessionId))
            || graph.Categories.Any(row => row.TenantId != tenantId || row.EventId != eventId)
            || graph.Tags.Any(row => row.TenantId != tenantId || row.EventId != eventId)
            || graph.SessionCategories.Any(row => row.TenantId != tenantId || !sessionIds.Contains(row.EventSessionId))
            || graph.SessionTags.Any(row => row.TenantId != tenantId || !sessionIds.Contains(row.EventSessionId))
            || graph.SessionLanguages.Any(row => row.TenantId != tenantId || !sessionIds.Contains(row.EventSessionId))
            || graph.SessionSpeakers.Any(row => row.TenantId != tenantId
                || !sessionIds.Contains(row.EventSessionId))
            || graph.CustomPropertyDefinitions.Any(row => row.TenantId != tenantId || row.EventId != eventId)
            || graph.SessionCustomPropertyDefinitions.Any(row => row.TenantId != tenantId
                || !sessionIds.Contains(row.EventSessionId));
    }

    private static Dictionary<PublicEventLocationDisclosureMemoKey, PublicEventLocationDisclosureInput> BuildDisclosureInputs(
        AtprotoEventPublicationEntityGraph graph,
        IReadOnlyList<EventSession> sessions,
        DateTimeOffset serverNowUtc,
        ICollection<string> errors)
    {
        Dictionary<Guid, EventLocation> placements = graph.EventLocations.ToDictionary(value => value.Id);
        var inputs = new Dictionary<PublicEventLocationDisclosureMemoKey, PublicEventLocationDisclosureInput>();

        void Add(Guid? eventLocationId, Guid? legacyLocationId, Guid? roomId, string source)
        {
            if (eventLocationId is null)
            {
                if (legacyLocationId.HasValue || roomId.HasValue)
                {
                    errors.Add($"{source} uses a raw location without an active EventLocation association.");
                }

                return;
            }

            if (!placements.TryGetValue(eventLocationId.Value, out EventLocation? placement))
            {
                errors.Add($"{source} references an EventLocation outside the loaded event graph.");
                return;
            }

            if (legacyLocationId.HasValue && legacyLocationId != placement.LocationId)
            {
                errors.Add($"{source} raw location does not match its EventLocation association.");
                return;
            }

            LocationRoom? room = null;
            if (roomId.HasValue)
            {
                room = placement.Location?.Rooms.SingleOrDefault(candidate => candidate.Id == roomId.Value);
                if (room is null)
                {
                    errors.Add($"{source} references a room outside its EventLocation.");
                    return;
                }
            }

            var input = new PublicEventLocationDisclosureInput(
                graph.Event.TenantId,
                graph.Event.Id,
                placement.Id,
                roomId,
                placement,
                placement.Location,
                room,
                serverNowUtc,
                Derivatives: null);
            inputs.TryAdd(input.MemoKey, input);
        }

        foreach (EventLocation placement in graph.EventLocations)
        {
            Add(placement.Id, placement.LocationId, roomId: null, "Event location");
        }

        foreach (EventSession session in sessions)
        {
            Add(session.EventLocationId, session.LocationId, session.RoomId, "Event session");
        }

        foreach (EventSessionGroup group in graph.SessionGroups.Where(group => group.IsPublished))
        {
            Add(group.EventLocationId, group.LocationId, group.RoomId, "Session group");
        }

        foreach (EventAgendaItem item in graph.AgendaItems)
        {
            Add(item.EventLocationId, item.LocationId, item.RoomId, "Event agenda item");
        }

        foreach (EventSessionAgendaItem item in graph.SessionAgendaItems
                     .Where(item => sessions.Any(session => session.Id == item.EventSessionId)))
        {
            Add(item.EventLocationId, item.LocationId, roomId: null, "Session agenda item");
        }

        return inputs;
    }

    private static AtprotoEventSessionSnapshot MapSession(
        AtprotoEventPublicationEntityGraph graph,
        EventSession session,
        Func<Guid?, Guid?, Guid?, AtprotoEventLocationSnapshot?> resolveLocation)
    {
        return new(
            SessionDisplayKey(session),
            Normalize(session.Title),
            Normalize(session.Description),
            session.StartTime?.ToUniversalTime(),
            session.EndTime?.ToUniversalTime(),
            session.LocalStartDate,
            session.LocalEndDate,
            session.LocalStartTime,
            session.LocalEndTime,
            session.EndTimeType.ToString(),
            session.SortOrder,
            Normalize(session.Slug),
            session.EventSessionKind is null ? null : LookupLabel(session.EventSessionKind.MasterCode, session.EventSessionKind.FullName, session.EventSessionKind.Description),
            LookupLabel(session.EventSessionStatus!.MasterCode, session.EventSessionStatus.FullName, session.EventSessionStatus.Description),
            session.RegistrationMode is null ? null : LookupLabel(session.RegistrationMode.MasterCode, session.RegistrationMode.FullName, session.RegistrationMode.Description),
            session.MaxAudienceAttendees,
            session.CurrentAudienceAttendees,
            PublicStorageDescription(session.FeaturedImage),
            resolveLocation(session.EventLocationId, session.LocationId, session.RoomId),
            MapSessionIslamicAspect(session.IslamicAspect),
            graph.SessionCategories
                .Where(link => link.EventSessionId == session.Id)
                .Select(link => LookupPath(link.Category))
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            graph.SessionTags
                .Where(link => link.EventSessionId == session.Id)
                .Select(link => LookupLabel(link.Tag.MasterCode, link.Tag.FullName))
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            graph.SessionLanguages
                .Where(link => link.EventSessionId == session.Id)
                .Select(link => LookupLabel(link.Language.MasterCode, link.Language.FullName, link.Language.Description))
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            graph.SessionSpeakers
                .Where(link => link.EventSessionId == session.Id)
                .OrderBy(link => link.Actor.Pii.DisplayName, StringComparer.Ordinal)
                .ThenBy(link => PrimaryAtprotoHandle(link.Actor), StringComparer.Ordinal)
                .ThenBy(link => link.Actor.Pii.ProfilePictureUri, StringComparer.Ordinal)
                .ThenBy(link => link.Actor.Description, StringComparer.Ordinal)
                .ThenBy(link => link.ActorId)
                .ThenBy(link => link.Id)
                .Select(link => new AtprotoSpeakerSnapshot(
                    Normalize(link.Actor.Pii.DisplayName)!,
                    Normalize(PrimaryAtprotoHandle(link.Actor)),
                    Normalize(link.Actor.Description),
                    Normalize(link.Actor.Pii.ProfilePictureUri),
                    null,
                    null,
                    Normalize(link.Actor.BackgroundColor),
                    Normalize(link.Actor.BackgroundEffect),
                    Normalize(link.Actor.BannerColor)))
                .ToImmutableArray(),
            graph.SessionAgendaItems
                .Where(item => item.EventSessionId == session.Id)
                .OrderBy(item => item.StartTime)
                .ThenBy(item => item.Title, StringComparer.Ordinal)
                .ThenBy(item => item.Id)
                .Select(item => new AtprotoSessionAgendaItemSnapshot(
                    item.StartTime.ToUniversalTime(),
                    item.EndTime.ToUniversalTime(),
                    Normalize(item.Title)!,
                    Normalize(item.Description),
                    resolveLocation(item.EventLocationId, item.LocationId, null)))
                .ToImmutableArray(),
            graph.SessionCustomPropertyDefinitions
                .Where(definition => definition.EventSessionId == session.Id && IsPublicCustomProperty(definition))
                .OrderBy(definition => definition.SortOrder)
                .ThenBy(definition => definition.DisplayName, StringComparer.Ordinal)
                .ThenBy(definition => definition.Namespace, StringComparer.Ordinal)
                .ThenBy(definition => definition.Key, StringComparer.Ordinal)
                .ThenBy(definition => definition.Id)
                .Select(MapCustomProperty)
                .ToImmutableArray());
    }

    private static AtprotoEventSessionGroupSnapshot MapGroup(
        AtprotoEventPublicationEntityGraph graph,
        EventSessionGroup group,
        Func<Guid?, Guid?, Guid?, AtprotoEventLocationSnapshot?> resolveLocation)
    {
        ImmutableArray<AtprotoEventSessionGroupMemberSnapshot> groupSessions = graph.SessionGroupSessions
            .Where(link => link.EventSessionGroupId == group.Id)
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.EventSessionId)
            .Select(link => graph.Sessions.SingleOrDefault(session => session.Id == link.EventSessionId))
            .Where(session => session is not null && IsPublicSession(session))
            .Select(session => new AtprotoEventSessionGroupMemberSnapshot(
                SessionDisplayKey(session!),
                graph.SessionGroupSessions.Single(link =>
                    link.EventSessionGroupId == group.Id && link.EventSessionId == session!.Id).IsPrimary,
                graph.SessionGroupSessions.Single(link =>
                    link.EventSessionGroupId == group.Id && link.EventSessionId == session!.Id).SortOrder))
            .ToImmutableArray();
        return new(
            Normalize(group.Name)!,
            Normalize(group.Description),
            Normalize(group.Slug),
            Normalize(group.Color),
            group.SortOrder,
            resolveLocation(group.EventLocationId, group.LocationId, group.RoomId),
            groupSessions);
    }

    private static AtprotoEventDetailsSnapshot MapDetails(Event eventEntity)
        => new(
            Normalize(eventEntity.Subtitle),
            eventEntity.EventType is null ? null : LookupLabel(eventEntity.EventType.MasterCode, eventEntity.EventType.FullName, eventEntity.EventType.Description),
            eventEntity.AudienceGender is null ? null : LookupLabel(eventEntity.AudienceGender.MasterCode, eventEntity.AudienceGender.FullName, eventEntity.AudienceGender.Description),
            eventEntity.AudienceAge is null ? null : string.Join(" | ", new[]
            {
                LookupLabel(eventEntity.AudienceAge.MasterCode, eventEntity.AudienceAge.FullName, eventEntity.AudienceAge.Description),
                eventEntity.AudienceAge.MinAge.HasValue ? $"Minimum age: {eventEntity.AudienceAge.MinAge.Value.ToString(CultureInfo.InvariantCulture)}" : null,
                eventEntity.AudienceAge.MaxAge.HasValue ? $"Maximum age: {eventEntity.AudienceAge.MaxAge.Value.ToString(CultureInfo.InvariantCulture)}" : null
            }.Where(value => value is not null)),
            eventEntity.TotalViews,
            LookupLabel(eventEntity.VisibilityType.MasterCode, eventEntity.VisibilityType.FullName, eventEntity.VisibilityType.Description),
            eventEntity.Madhab is null ? null : LookupLabel(eventEntity.Madhab.MasterCode, eventEntity.Madhab.FullName, eventEntity.Madhab.Description),
            Normalize(eventEntity.GetEffectiveScheduleTimeZoneId())!,
            eventEntity.SessionCount,
            eventEntity.RegistrationPolicy is null ? null : LookupLabel(eventEntity.RegistrationPolicy.MasterCode, eventEntity.RegistrationPolicy.FullName, eventEntity.RegistrationPolicy.Description),
            Normalize(eventEntity.Slug),
            Normalize(eventEntity.PublicCode) ?? string.Empty,
            eventEntity.FirstSessionDate,
            eventEntity.LastSessionDate,
            eventEntity.LastSessionStartUtc?.ToUniversalTime());

    private static AtprotoOrganizerSnapshot MapOrganizer(Actor actor)
    {
        Organization? organization = actor.Organization is { IsDeleted: false } activeOrganization
            ? activeOrganization
            : null;
        return new(
            Normalize(actor.Pii.DisplayName)!,
            LookupLabel(actor.ActorType.MasterCode, actor.ActorType.FullName, actor.ActorType.Description),
            Normalize(PrimaryAtprotoHandle(actor)),
            Normalize(actor.Description),
            Normalize(organization?.Pii.FullName),
            Normalize(organization?.WebsiteUrl),
            Normalize(organization?.Pii.Country),
            Normalize(organization?.Pii.City),
            Normalize(actor.Group?.FullName),
            Normalize(actor.Group?.Description),
            Normalize(actor.Pii.ProfilePictureUri),
            null,
            null,
            Normalize(actor.BackgroundColor),
            Normalize(actor.BackgroundEffect),
            Normalize(actor.BannerColor),
            null);
    }

    private static AtprotoEventSeriesSnapshot? MapSeries(Explore.Domain.EventSeries? series, int? eventOrder)
        => series is null
            || series.IsDeleted
            || !series.IsPublished
            || series.VisibilityTypeId != (int)VisibilityTypeEnum.Public
            ? null
            : new(
                Normalize(series.Title)!,
                Normalize(series.Description),
                Normalize(series.Slug),
                series.IsPublished,
                series.TotalViews,
                LookupLabel(series.VisibilityType.MasterCode, series.VisibilityType.FullName, series.VisibilityType.Description),
                series.StartDateUtc?.ToUniversalTime(),
                series.EndDateUtc?.ToUniversalTime(),
                eventOrder,
                Normalize(series.Actor?.Pii.DisplayName) ?? string.Empty,
                Normalize(series.Actor is null ? null : PrimaryAtprotoHandle(series.Actor)),
                PublicStorageDescription(series.FeaturedImage));

    private static string? PrimaryAtprotoHandle(Actor actor)
        => actor.AtprotoIdentities
            .Where(identity => identity.IsActive && !identity.IsDeleted && !identity.IsSuspended)
            .OrderByDescending(identity => identity.LastResolvedAt)
            .ThenBy(identity => identity.Did, StringComparer.Ordinal)
            .Select(identity => identity.Handle)
            .FirstOrDefault(handle => !string.IsNullOrWhiteSpace(handle));

    private static AtprotoEventDaySnapshot MapDay(EventDay day)
        => new(
            day.LocalDate,
            Normalize(day.Label),
            Normalize(day.Description),
            Normalize(day.BannerText),
            PublicStorageDescription(day.BannerImage),
            day.AllowsDayScopeRegistration,
            day.SortOrder);

    private static AtprotoEventIslamicAspectSnapshot? MapIslamicAspect(EventIslamicAspect? aspect)
        => aspect is null
            ? null
            : new(
                aspect.Madhab is null ? null : LookupLabel(aspect.Madhab.MasterCode, aspect.Madhab.FullName, aspect.Madhab.Description),
                aspect.ReferencePrayer?.ToString(),
                aspect.PrayerTimeOffset,
                aspect.GenderMode.ToString(),
                aspect.IncludesQuranRecitation,
                aspect.PrimaryLanguage is null ? null : LookupLabel(aspect.PrimaryLanguage.MasterCode, aspect.PrimaryLanguage.FullName, aspect.PrimaryLanguage.Description));

    private static AtprotoSessionIslamicAspectSnapshot? MapSessionIslamicAspect(EventSessionIslamicAspect? aspect)
        => aspect is null
            ? null
            : new(
                aspect.StartTimeType.ToString(),
                aspect.ReferencePrayer?.ToString(),
                aspect.OffsetMinutes,
                aspect.EndReferencePrayer?.ToString(),
                aspect.EndOffsetMinutes,
                aspect.RequiresWudu,
                NormalizeRichText(aspect.RitualRequirementsJson));

    private static AtprotoEventTechAspectSnapshot? MapTechAspect(EventTechAspect? aspect)
        => aspect is null
            ? null
            : new(
                Normalize(aspect.GithubRepoUrl),
                Normalize(aspect.HackathonTrack),
                aspect.SkillLevel.ToString(),
                (aspect.TechStackTags ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Order(StringComparer.Ordinal)
                    .ToImmutableArray(),
                aspect.RequiresLaptop,
                aspect.IsCodingCompetition,
                aspect.MaxTeamSize,
                aspect.PrizePool,
                Normalize(aspect.PrizeCurrencyCode));

    private static AtprotoCustomPropertySnapshot MapCustomProperty(EventCustomPropertyDefinition definition)
        => new(
            Normalize(definition.Namespace)!,
            Normalize(definition.Key)!,
            Normalize(definition.DisplayName)!,
            Normalize(definition.Description),
            definition.PropertyType.ToString(),
            definition.IsRequired,
            definition.IsMulti,
            definition.IsActive,
            definition.SortOrder,
            definition.ExposureLevel.ToString(),
            definition.IsSearchable,
            definition.IsFilterable,
            definition.IsExportable,
            definition.IsModerationRelevant,
            definition.IsAnalyticsRelevant,
            definition.IsSystemOwned,
            DefaultCustomPropertyValue(
                definition.DefaultOption?.DisplayName,
                definition.DefaultTextValue,
                definition.DefaultNumberValue,
                definition.DefaultBooleanValue,
                definition.DefaultDateTimeValue),
            definition.MinLength,
            definition.MaxLength,
            Normalize(definition.RegexPattern),
            definition.MinNumber,
            definition.MaxNumber,
            definition.MinDateTime?.ToUniversalTime(),
            definition.MaxDateTime?.ToUniversalTime(),
            Normalize(definition.AllowedUrlSchemes),
            definition.Options
                .Where(option => !option.IsDeleted)
                .OrderBy(option => option.SortOrder)
                .ThenBy(option => option.Namespace, StringComparer.Ordinal)
                .ThenBy(option => option.Key, StringComparer.Ordinal)
                .ThenBy(option => option.Id)
                .Select(option => MapOption(
                    option,
                    definition.Options.SingleOrDefault(candidate => candidate.Id == option.ParentOptionId)?.DisplayName))
                .ToImmutableArray(),
            definition.Values
                .Where(value => !value.IsDeleted)
                .OrderBy(value => value.Ordinal)
                .ThenBy(value => value.Id)
                .Select(MapValue)
                .ToImmutableArray());

    private static AtprotoCustomPropertySnapshot MapCustomProperty(EventSessionCustomPropertyDefinition definition)
        => new(
            Normalize(definition.Namespace)!,
            Normalize(definition.Key)!,
            Normalize(definition.DisplayName)!,
            Normalize(definition.Description),
            definition.PropertyType.ToString(),
            definition.IsRequired,
            definition.IsMulti,
            definition.IsActive,
            definition.SortOrder,
            definition.ExposureLevel.ToString(),
            definition.IsSearchable,
            definition.IsFilterable,
            definition.IsExportable,
            definition.IsModerationRelevant,
            definition.IsAnalyticsRelevant,
            definition.IsSystemOwned,
            DefaultCustomPropertyValue(
                definition.DefaultOption?.DisplayName,
                definition.DefaultTextValue,
                definition.DefaultNumberValue,
                definition.DefaultBooleanValue,
                definition.DefaultDateTimeValue),
            definition.MinLength,
            definition.MaxLength,
            Normalize(definition.RegexPattern),
            definition.MinNumber,
            definition.MaxNumber,
            definition.MinDateTime?.ToUniversalTime(),
            definition.MaxDateTime?.ToUniversalTime(),
            Normalize(definition.AllowedUrlSchemes),
            definition.Options
                .Where(option => !option.IsDeleted)
                .OrderBy(option => option.SortOrder)
                .ThenBy(option => option.Namespace, StringComparer.Ordinal)
                .ThenBy(option => option.Key, StringComparer.Ordinal)
                .ThenBy(option => option.Id)
                .Select(option => MapOption(
                    option,
                    definition.Options.SingleOrDefault(candidate => candidate.Id == option.ParentOptionId)?.DisplayName))
                .ToImmutableArray(),
            definition.Values
                .Where(value => !value.IsDeleted)
                .OrderBy(value => value.Ordinal)
                .ThenBy(value => value.Id)
                .Select(MapValue)
                .ToImmutableArray());

    private static AtprotoCustomPropertyOptionSnapshot MapOption(
        EventCustomPropertyOption option,
        string? parentDisplayName)
        => new(
            Normalize(option.Namespace)!,
            Normalize(option.Key)!,
            Normalize(option.DisplayName)!,
            Normalize(option.Description),
            Normalize(option.Value)!,
            option.IsDefault,
            option.IsActive,
            option.SortOrder,
            Normalize(parentDisplayName));

    private static AtprotoCustomPropertyOptionSnapshot MapOption(
        EventSessionCustomPropertyOption option,
        string? parentDisplayName)
        => new(
            Normalize(option.Namespace)!,
            Normalize(option.Key)!,
            Normalize(option.DisplayName)!,
            Normalize(option.Description),
            Normalize(option.Value)!,
            option.IsDefault,
            option.IsActive,
            option.SortOrder,
            Normalize(parentDisplayName));

    private static AtprotoCustomPropertyValueSnapshot MapValue(EventCustomPropertyValue value)
        => CreateValueSnapshot(
            value.Ordinal,
            value.Option?.DisplayName,
            value.TextValue,
            value.NumberValue,
            value.BooleanValue,
            value.DateTimeValue);

    private static AtprotoCustomPropertyValueSnapshot MapValue(EventSessionCustomPropertyValue value)
        => CreateValueSnapshot(
            value.Ordinal,
            value.Option?.DisplayName,
            value.TextValue,
            value.NumberValue,
            value.BooleanValue,
            value.DateTimeValue);

    private static AtprotoCustomPropertyValueSnapshot CreateValueSnapshot(
        int ordinal,
        string? optionDisplayName,
        string? textValue,
        decimal? numberValue,
        bool? booleanValue,
        DateTimeOffset? dateTimeValue)
    {
        string? option = Normalize(optionDisplayName);
        if (option is not null)
        {
            return new(ordinal, "Option", option, option);
        }

        if (Normalize(textValue) is { } text)
        {
            return new(ordinal, "Text", text, null);
        }

        if (numberValue.HasValue)
        {
            return new(ordinal, "Number", numberValue.Value.ToString(CultureInfo.InvariantCulture), null);
        }

        if (booleanValue.HasValue)
        {
            return new(ordinal, "Boolean", booleanValue.Value.ToString(CultureInfo.InvariantCulture), null);
        }

        return dateTimeValue.HasValue
            ? new(ordinal, "DateTime", dateTimeValue.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), null)
            : new(ordinal, "Unset", "Unset", null);
    }

    private static string? DefaultCustomPropertyValue(
        string? optionDisplayName,
        string? textValue,
        decimal? numberValue,
        bool? booleanValue,
        DateTimeOffset? dateTimeValue)
        => Normalize(optionDisplayName)
            ?? Normalize(textValue)
            ?? numberValue?.ToString(CultureInfo.InvariantCulture)
            ?? booleanValue?.ToString(CultureInfo.InvariantCulture)
            ?? dateTimeValue?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static ImmutableArray<AtprotoEventUriSnapshot> BuildUris(Event eventEntity)
    {
        var values = new List<AtprotoEventUriSnapshot>();
        EventPublicAction? externalRegistration = eventEntity.ParticipationConfiguration is { } participationConfiguration
            ? eventEntity.PublicActions
                .Where(action => !action.IsDeleted
                    && action.EventPublicActionKindId == (int)EventPublicActionKindEnum.ExternalRegistration
                    && action.HealthStateId == (int)EventPublicActionHealthStateEnum.Active
                    && EventAuthorityRules.IsPublicActionAllowed(
                        participationConfiguration.ParticipationHandlingModeId,
                        action.EventPublicActionKindId))
                .OrderByDescending(action => action.IsPrimary)
                .ThenBy(action => action.SortOrder)
                .ThenBy(action => action.Id)
                .FirstOrDefault()
            : null;
        AddUri(values, externalRegistration?.Url, "Registration");
        AddUri(values, PublicStorageUri(eventEntity.FeaturedImage), "Featured image");
        AddUri(values, PublicStorageUri(eventEntity.BackgroundImage), "Background image");
        return values
            .Distinct()
            .OrderBy(value => value.Uri, StringComparer.Ordinal)
            .ThenBy(value => value.Name, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void AddUri(ICollection<AtprotoEventUriSnapshot> values, string? uri, string name)
    {
        string? normalized = Normalize(uri);
        if (normalized is not null)
        {
            values.Add(new(normalized, name));
        }
    }

    private static AtprotoEventLocationSnapshot? MapLocation(EventLocationDisclosureResult result)
    {
        if (result.Purpose != EventLocationDisclosurePurpose.Public)
        {
            return null;
        }

        EventLocationDisclosureValues? values = result.Values;
        return values is null
            ? result.State == EventLocationDisclosureState.ToBeAnnounced
                ? new(
                    result.State,
                    Country: null,
                    TimeZone: null,
                    City: null,
                    VenueName: "To be announced",
                    RoomName: null,
                    RoomDescription: null,
                    StreetAddress: null,
                    Postcode: null,
                    Latitude: null,
                    Longitude: null,
                    FormattedAddress: null,
                    MapUri: null,
                    Geohash: null)
                : null
            : new(
                result.State,
                Normalize(values.Country),
                Normalize(values.Timezone),
                Normalize(values.City),
                Normalize(values.VenueName),
                Normalize(values.RoomName),
                Normalize(values.RoomDescription),
                Normalize(values.StreetAddress),
                Normalize(values.Postcode),
                values.Latitude,
                values.Longitude,
                Normalize(values.FormattedAddress),
                Normalize(values.MapUrl),
                Normalize(values.Geohash));
    }

    private static string LocationSortKey(AtprotoEventLocationSnapshot location)
        => string.Join('|',
            location.State,
            location.Country,
            location.City,
            location.VenueName,
            location.RoomName,
            location.StreetAddress,
            location.Postcode,
            location.Latitude?.ToString("R", CultureInfo.InvariantCulture),
            location.Longitude?.ToString("R", CultureInfo.InvariantCulture));

    private static string LookupPath(Category category)
        => category.Parent is null
            ? LookupLabel(category.MasterCode, category.FullName)
            : $"{LookupLabel(category.Parent.MasterCode, category.Parent.FullName)} / {LookupLabel(category.MasterCode, category.FullName)}";

    private static string LookupLabel(string? masterCode, string fullName, string? description = null)
    {
        string label = Normalize(fullName)!;
        string? code = Normalize(masterCode);
        string? detail = Normalize(description);
        return string.Join(" | ", new[]
        {
            label,
            code is null ? null : $"Code: {code}",
            detail is null ? null : $"Description: {detail}"
        }.Where(value => value is not null));
    }

    private static string SessionDisplayKey(EventSession session)
        => Normalize(session.Title)
            ?? session.StartTime?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            ?? $"Session {session.SortOrder.ToString(CultureInfo.InvariantCulture)}";

    private static bool IsPublicSession(EventSession session)
        => !session.IsDeleted
            && session.EventSessionStatusId == (int)EventSessionStatusEnum.Published;

    private static bool IsPublicCustomProperty(EventCustomPropertyDefinition definition)
        => definition.IsActive
            && !definition.IsDeleted
            && definition.ExposureLevel == ExposureLevel.Public;

    private static bool IsPublicCustomProperty(EventSessionCustomPropertyDefinition definition)
        => definition.IsActive
            && !definition.IsDeleted
            && definition.ExposureLevel == ExposureLevel.Public;

    private static string? PublicStorageUri(StorageObject? storageObject)
        => storageObject is not null
            && !storageObject.IsDeleted
            && string.Equals(storageObject.Visibility, StorageObjectVisibilities.PublicImage, StringComparison.Ordinal)
            && string.Equals(storageObject.LifecycleState, StorageObjectLifecycleStates.Active, StringComparison.Ordinal)
                ? Normalize(storageObject.Uri)
                : null;

    private static string? PublicStorageDescription(StorageObject? storageObject)
    {
        string? uri = PublicStorageUri(storageObject);
        if (uri is null || storageObject is null)
        {
            return null;
        }

        return string.Join(" | ", new[]
        {
            uri,
            $"Name: {Normalize(storageObject.SafeDisplayName)}",
            $"Extension: {Normalize(storageObject.Extension)}",
            Normalize(storageObject.ContentType) is { } contentType ? $"Content type: {contentType}" : null,
            $"Size: {storageObject.Size.ToString(CultureInfo.InvariantCulture)} bytes",
            $"Purpose: {Normalize(storageObject.Purpose)}",
            storageObject.FileType is null ? null : $"File type: {LookupLabel(storageObject.FileType.MasterCode, storageObject.FileType.FullName)}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? MapMode(string masterCode)
        => masterCode.ToUpperInvariant() switch
        {
            "LOCAL" => "community.lexicon.calendar.event#inperson",
            "DIGITAL" => "community.lexicon.calendar.event#virtual",
            "HYBRID" => "community.lexicon.calendar.event#hybrid",
            _ => null
        };

    private static string? MapStatus(string masterCode)
        => masterCode.ToUpperInvariant() switch
        {
            "DRAFT" => "community.lexicon.calendar.event#planned",
            "CANCELLED" => "community.lexicon.calendar.event#cancelled",
            "PUBLISHED" or "COMPLETED" or "ARCHIVED" => "community.lexicon.calendar.event#scheduled",
            _ => null
        };

    private static DateTimeOffset ToUtc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string? NormalizeRichText(string? value)
    {
        string? normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        string withoutMarkup = HtmlTagRegex().Replace(normalized, " ");
        return Normalize(WebUtility.HtmlDecode(withoutMarkup));
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();
}
