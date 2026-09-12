using System.Text.Json;
using System.Reflection;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;

namespace Event.Application.UnitTests.Profiles;

[Category("EventSessionMapping")]
public sealed class EventSessionMapperTests
{
    private static readonly Guid SessionId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly Guid Stamp = Guid.Parse("01900000-0000-7000-8000-000000000004");
    private static readonly DateTimeOffset Start = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(typeof(EventSessionDto))]
    [Arguments(typeof(EventSessionListDto))]
    [Arguments(typeof(EventSessionGroupDto))]
    public async Task ParentTitle_DeclaresTheExistingNullableOutputContract(Type contract)
    {
        var property = contract.GetProperty(nameof(EventSessionDto.EventTitle))!;
        await Assert.That(new NullabilityInfoContext().Create(property).ReadState)
            .IsEqualTo(NullabilityState.Nullable);
    }

    [Test]
    public async Task DetailMapping_ProjectsParentEventStatusId()
    {
        var session = Session(EventStatusEnum.Cancelled);
        await Assert.That(EventSessionMapper.ToDetail(session).ParentEventStatusId).IsEqualTo((int)EventStatusEnum.Cancelled);
    }

    [Test]
    public async Task ListMapping_ProjectsParentEventStatusId()
    {
        var session = Session(EventStatusEnum.Archived);
        await Assert.That(EventSessionMapper.ToListItem(session).ParentEventStatusId).IsEqualTo((int)EventStatusEnum.Archived);
    }

    [Test]
    public async Task OpenEndedSchedule_ProjectsAsScheduledWithoutInventedEnd()
    {
        var session = Session();
        session.ScheduleOpenEnded(Start, "UTC", new EventScheduleProjectionCalculator());
        var detail = EventSessionMapper.ToDetail(session);
        var list = EventSessionMapper.ToListItem(session);
        await Assert.That(detail.IsScheduled && list.IsScheduled).IsTrue();
        await Assert.That(detail.EndTime).IsNull();
        await Assert.That(list.EndTime).IsNull();
        await Assert.That(detail.FormattedEndTime).IsEqualTo("Open-ended");
        await Assert.That(list.LocalEndTime).IsNull();
    }

    [Test]
    [Arguments(SessionEndTimeType.Fixed, false, false, false)]
    [Arguments(SessionEndTimeType.Fixed, true, false, false)]
    [Arguments(SessionEndTimeType.Fixed, true, true, true)]
    [Arguments(SessionEndTimeType.OpenEnded, true, true, false)]
    [Arguments(SessionEndTimeType.RelativeToPrayer, true, false, true)]
    [Arguments(SessionEndTimeType.RelativeToPrayer, true, true, true)]
    [Arguments((SessionEndTimeType)99, true, true, false)]
    public async Task ScheduleValidity_IsPreserved(SessionEndTimeType kind, bool hasStart, bool hasEnd, bool expected)
    {
        var session = Session();
        session.StartTime = hasStart ? Start : null;
        session.EndTime = hasEnd ? Start.AddHours(1) : null;
        session.EndTimeType = kind;
        await Assert.That(EventSessionMapper.ToDetail(session).IsScheduled).IsEqualTo(expected);
        await Assert.That(EventSessionMapper.ToListItem(session).IsScheduled).IsEqualTo(expected);
    }

    [Test]
    [Arguments(-15, "Until 15 minutes before Fajr")]
    [Arguments(0, "Until Fajr prayer")]
    [Arguments(20, "Until 20 minutes after Fajr")]
    public async Task PrayerFormatting_AndAspectSnapshot_ArePreserved(int offset, string expected)
    {
        var session = Session();
        session.EndTimeType = SessionEndTimeType.RelativeToPrayer;
        session.IslamicAspect = new EventSessionIslamicAspect
        {
            EventSessionId = SessionId, EventSession = session,
            StartTimeType = SessionStartTimeType.RelativeToPrayer,
            ReferencePrayer = PrayerTime.Dhuhr, OffsetMinutes = 5,
            EndReferencePrayer = PrayerTime.Fajr, EndOffsetMinutes = offset,
            RequiresWudu = true, RitualRequirementsJson = "{\"ritual\":true}"
        };
        var detail = EventSessionMapper.ToDetail(session);
        var list = EventSessionMapper.ToListItem(session);
        session.IslamicAspect.RequiresWudu = false;
        await Assert.That(detail.FormattedEndTime).IsEqualTo(expected);
        await Assert.That(list.FormattedEndTime).IsEqualTo(expected);
        await Assert.That(detail.IslamicAspect!.RequiresWudu).IsTrue();
        await Assert.That(list.IslamicAspect!.ReferencePrayer).IsEqualTo(PrayerTime.Dhuhr);
        await Assert.That(detail.IslamicAspect.OffsetMinutes).IsEqualTo(5);
        await Assert.That(list.IslamicAspect.RitualRequirementsJson).IsEqualTo("{\"ritual\":true}");
    }

    [Test]
    public async Task MissingNavigations_PreserveNullsAndDefaultParentStatus()
    {
        var session = Session();
        session.Event = null!;
        session.EndTimeType = SessionEndTimeType.RelativeToPrayer;
        var detail = EventSessionMapper.ToDetail(session);
        var list = EventSessionMapper.ToListItem(session);
        await Assert.That(detail.EventTitle).IsNull();
        await Assert.That(list.EventTitle).IsNull();
        await Assert.That(detail.ParentEventStatusId).IsEqualTo(0);
        await Assert.That(list.EventSessionKindFullName).IsNull();
        await Assert.That(detail.EventSessionStatusMasterCode).IsNull();
        await Assert.That(detail.RegistrationModeMasterCode).IsNull();
        await Assert.That(list.FeaturedImageUri).IsNull();
        await Assert.That(detail.IslamicAspect).IsNull();
        await Assert.That(detail.FormattedEndTime).IsEqualTo("Relative to prayer");
        await Assert.That(list.SessionGroups.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ScalarLookupsDatesAndJson_PreserveTransportWithoutEntityGraphs()
    {
        var session = Session();
        session.StartTime = Start;
        session.EndTime = Start.AddMinutes(75);
        session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        session.EventSessionKindId = 7;
        session.EventSessionKind = new EventSessionKind { Id = 7, FullName = "Lecture", MasterCode = "LECTURE" };
        session.EventSessionStatus = new EventSessionStatus { FullName = "Draft", MasterCode = "DRAFT" };
        session.RegistrationModeId = 8;
        session.RegistrationMode = new RegistrationMode { FullName = "Required", MasterCode = "REQUIRED" };
        session.LocationId = Stamp;
        session.RoomId = Stamp;
        session.Description = "Session description";
        session.Slug = "session-slug";
        session.SortOrder = 3;
        session.MaxAudienceAttendees = 40;
        session.CurrentAudienceAttendees = 9;
        var detail = EventSessionMapper.ToDetail(session);
        var list = EventSessionMapper.ToListItem(session);
        await Assert.That(detail.Id).IsEqualTo(SessionId);
        await Assert.That(detail.EventId).IsEqualTo(EventId);
        await Assert.That(detail.TenantId).IsEqualTo(TenantId);
        await Assert.That(detail.ConcurrencyStamp).IsEqualTo(Stamp);
        await Assert.That(list.ConcurrencyStamp).IsEqualTo(Stamp);
        await Assert.That(detail.EventTitle).IsEqualTo("Parent event");
        await Assert.That(detail.EventSessionKindMasterCode).IsEqualTo("LECTURE");
        await Assert.That(list.EventSessionKindFullName).IsEqualTo("Lecture");
        await Assert.That(list.EventSessionStatusMasterCode).IsEqualTo("DRAFT");
        await Assert.That(detail.RegistrationModeMasterCode).IsEqualTo("REQUIRED");
        await Assert.That(list.RegistrationModeFullName).IsEqualTo("Required");
        await Assert.That(detail.LocalStartDate).IsEqualTo(new DateOnly(2026, 6, 15));
        await Assert.That(detail.LocalEndDate).IsEqualTo(new DateOnly(2026, 6, 15));
        await Assert.That(detail.LocalStartMinuteOfDay).IsEqualTo(600);
        await Assert.That(detail.LocalEndMinuteOfDay).IsEqualTo(675);
        await Assert.That(list.LocalEndTime).IsEqualTo(new TimeOnly(11, 15));
        await Assert.That(detail.FormattedEndTime).IsEqualTo("11:15");
        await Assert.That(list.FormattedEndTime).IsEqualTo("11:15");
        await Assert.That(detail.LocationId).IsNull();
        await Assert.That(list.RoomId).IsNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(detail));
        await Assert.That(json.RootElement.GetProperty("EndTimeType").GetInt32()).IsEqualTo(0);
        await Assert.That(json.RootElement.GetProperty("Description").GetString()).IsEqualTo("Session description");
        await Assert.That(json.RootElement.GetProperty("SessionGroups").ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(json.RootElement.TryGetProperty("Event", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("Tenant", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("CreatedAt", out _)).IsFalse();
    }

    [Test]
    public async Task CyclicGroups_AreFilteredOrderedAndSnapshotted()
    {
        var session = Session();
        var secondary = Assignment(session, "Secondary", true, false, 0);
        var primaryLater = Assignment(session, "Primary later", true, true, 2);
        var hidden = Assignment(session, "Hidden", false, true, -1);
        var primaryFirst = Assignment(session, "Primary first", true, true, 1);
        session.SessionGroups = [secondary, primaryLater, hidden, primaryFirst];
        var detail = EventSessionMapper.ToDetail(session);
        var list = EventSessionMapper.ToListItem(session);
        session.SessionGroups.Clear();
        primaryFirst.EventSessionGroup.Name = "Changed";
        await Assert.That(detail.SessionGroups.Select(x => x.Name).ToArray()).IsEquivalentTo(new[] { "Primary first", "Primary later", "Secondary" });
        await Assert.That(detail.SessionGroups[0].Name).IsEqualTo("Primary first");
        await Assert.That(detail.SessionGroups[1].Name).IsEqualTo("Primary later");
        await Assert.That(list.SessionGroups.Count).IsEqualTo(3);
        await Assert.That(list.SessionGroups[2].Name).IsEqualTo("Secondary");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(detail));
        await Assert.That(json.RootElement.GetProperty("SessionGroups")[0].GetProperty("Name").GetString()).IsEqualTo("Primary first");
        await Assert.That(json.RootElement.GetProperty("SessionGroups")[0].TryGetProperty("EventSession", out _)).IsFalse();
    }

    [Test]
    public async Task GroupAgendaSpeakerAndLanguage_PreserveTheirScalarBoundaries()
    {
        var session = Session();
        var group = Assignment(session, "Track", true, true, 2).EventSessionGroup;
        group.ConcurrencyStamp = Stamp;
        group.LocationId = Stamp;
        await Assert.That(EventSessionMapper.ToDetail(group).EventTitle).IsEqualTo("Parent event");
        await Assert.That(EventSessionMapper.ToListItem(group).ConcurrencyStamp).IsEqualTo(Stamp);
        await Assert.That(EventSessionMapper.ToDetail(group).LocationId).IsNull();
        var agenda = new EventSessionAgendaItem
        {
            Id = Stamp, EventSessionId = SessionId, EventSession = session,
            TenantId = TenantId, Tenant = null!, Title = "Agenda", Description = "Notes",
            StartTime = Start, EndTime = Start.AddMinutes(5), LocationId = Stamp
        };
        await Assert.That(EventSessionMapper.ToDetail(agenda).EventId).IsEqualTo(EventId);
        await Assert.That(EventSessionMapper.ToListItem(agenda).EventSessionTitle).IsEqualTo("Session");
        await Assert.That(EventSessionMapper.ToDetail(agenda).LocationId).IsNull();
        await Assert.That(EventSessionMapper.ToListItem(agenda).EndTime).IsEqualTo(Start.AddMinutes(5));
        var speaker = new EventSessionSpeaker
        {
            Id = Stamp, ConcurrencyStamp = Stamp, EventSessionId = SessionId, EventSession = session,
            ActorId = EventId, Actor = new Actor { ActorType = null!, Pii = new ActorPii { DisplayName = "Speaker" } },
            TenantId = TenantId, Tenant = null!
        };
        await Assert.That(EventSessionMapper.ToDetail(speaker).ActorDisplayName).IsEqualTo("Speaker");
        await Assert.That(EventSessionMapper.ToListItem(speaker).EventId).IsEqualTo(EventId);
        speaker.Actor.Pii = null!;
        speaker.EventSession = null!;
        await Assert.That(EventSessionMapper.ToDetail(speaker).ActorDisplayName).IsNull();
        await Assert.That(EventSessionMapper.ToListItem(speaker).EventId).IsEqualTo(Guid.Empty);
        var language = new EventSessionLanguage
        {
            Id = 12, ConcurrencyStamp = Stamp, EventSessionId = SessionId, EventSession = session,
            LanguageId = 4, Language = new Language { FullName = "Arabic", MasterCode = "ar" },
            TenantId = TenantId, Tenant = null!
        };
        await Assert.That(EventSessionMapper.ToDetail(language).LanguageMasterCode).IsEqualTo("ar");
        await Assert.That(EventSessionMapper.ToListItem(language).LanguageFullName).IsEqualTo("Arabic");
        await Assert.That(EventSessionMapper.ToListItem(language).EventId).IsEqualTo(Guid.Empty);
        await Assert.That(EventSessionMapper.ToDetail(language).Id).IsEqualTo(12);
        language.Language = null!;
        language.EventSession = null!;
        await Assert.That(EventSessionMapper.ToDetail(language).LanguageFullName).IsNull();
        await Assert.That(EventSessionMapper.ToListItem(language).EventSessionTitle).IsNull();
    }

    private static EventSession Session(EventStatusEnum parentStatus = EventStatusEnum.Published) => new(EventSessionStatusEnum.Draft)
    {
        Id = SessionId, EventId = EventId, TenantId = TenantId, Tenant = null!, ConcurrencyStamp = Stamp, Title = "Session",
        Event = new Explore.Domain.Event(parentStatus)
        {
            Id = EventId, Title = "Parent event", TenantId = TenantId,
            Actor = null!, Tenant = null!, VisibilityType = null!, EventStatus = null!, EventFormat = null!
        }
    };

    private static EventSessionGroupSession Assignment(EventSession session, string name, bool published, bool primary, int order)
    {
        var group = new EventSessionGroup
        {
            Id = Guid.Parse($"01900000-0000-7000-8000-{order + 2:000000000000}"),
            EventId = EventId, Event = session.Event, TenantId = TenantId, Tenant = null!,
            Name = name, Slug = "track", Color = "blue", IsPublished = published
        };
        var assignment = new EventSessionGroupSession
        {
            EventSessionGroupId = group.Id, EventSessionGroup = group,
            EventSessionId = SessionId, EventSession = session, EventId = EventId, Event = session.Event,
            TenantId = TenantId, Tenant = null!, IsPrimary = primary, SortOrder = order
        };
        group.Sessions.Add(assignment);
        return assignment;
    }
}
