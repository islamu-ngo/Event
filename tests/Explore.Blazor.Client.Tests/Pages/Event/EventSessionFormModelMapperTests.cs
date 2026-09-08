using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Events.Sessions;
using ComposerCreateEventSessionRequest = Explore.Blazor.Client.Clients.CreateEventSessionDto;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class EventSessionFormModelMapperTests
{
    private const string BrusselsTimeZoneId = "Europe/Brussels";

    [Test]
    public async Task ApplyCreateContext_UsesDefaultsAndSelectorOptions()
    {
        var session = new ComposerCreateEventSessionRequest();
        var locationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var context = new EventSessionCreateContextDto
        {
            Defaults = new EventSessionCreateDefaultsDto
            {
                RegistrationModeId = 2,
                SessionDate = new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero),
                StartTime = "09:15:00",
                EndTime = "10:45:00"
            },
            Locations = [new EventSessionCreateLocationOptionDto { Id = locationId, FullName = "Main Hall" }],
            SessionGroups = [new EventSessionCreateGroupOptionDto { Id = groupId, Name = "Main track" }]
        };

        var state = EventSessionFormModelMapper.ApplyCreateContext(session, context, fallbackRegistrationModeId: 1);

        await Assert.That(session.RegistrationModeId).IsEqualTo(2);
        await Assert.That(state.Locations.Single().Id).IsEqualTo(locationId);
        await Assert.That(state.SessionGroups.Single().Id).IsEqualTo(groupId);
        await Assert.That(state.SessionDate).IsEqualTo(new DateTime(2026, 5, 20));
        await Assert.That(state.StartTime).IsEqualTo(new TimeSpan(9, 15, 0));
        await Assert.That(state.EndTime).IsEqualTo(new TimeSpan(10, 45, 0));
    }

    [Test]
    public async Task TryPrepareCreateRequest_NormalizesScheduleAndCapacity()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var session = new ComposerCreateEventSessionRequest
        {
            Title = "  Opening talk  ",
            MaxAudienceAttendees = 0
        };

        var prepared = EventSessionFormModelMapper.TryPrepareCreateRequest(
            session,
            eventId,
            tenantId,
            new DateTime(2026, 6, 1),
            new TimeSpan(9, 30, 0),
            new TimeSpan(10, 30, 0),
            out var validationError,
            BrusselsTimeZoneId);

        await Assert.That(prepared).IsTrue();
        await Assert.That(validationError).IsNull();
        await Assert.That(session.EventId).IsEqualTo(eventId);
        await Assert.That(session.Title).IsEqualTo("Opening talk");
        await Assert.That(session.MaxAudienceAttendees).IsNull();
        await Assert.That(session.StartTime).IsEqualTo(new DateTimeOffset(2026, 6, 1, 7, 30, 0, TimeSpan.Zero));
        await Assert.That(session.EndTime).IsEqualTo(new DateTimeOffset(2026, 6, 1, 8, 30, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task PopulateUpdateRequest_MapsEditableSessionFieldsAndPrimaryGroup()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var primaryGroupId = Guid.NewGuid();
        var secondaryGroupId = Guid.NewGuid();
        var request = new UpdateEventSessionDto();
        var source = new EventSessionDto
        {
            Id = sessionId,
            EventId = eventId,
            Title = "Workshop",
            Description = "Practical work",
            Slug = "workshop",
            MaxAudienceAttendees = 30,
            RegistrationModeId = 2,
            StartTime = new DateTimeOffset(2026, 7, 3, 14, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 7, 3, 15, 30, 0, TimeSpan.Zero),
            LocalStartDate = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero),
            LocalEndDate = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero),
            LocalStartTime = new TimeSpan(16, 0, 0),
            LocalEndTime = new TimeSpan(17, 30, 0),
            IslamicAspect = new EventSessionIslamicAspectDto { ReferencePrayer = (PrayerTime)2, RequiresWudu = true },
            SessionGroups =
            [
                new SessionGroups { EventSessionGroupId = secondaryGroupId, IsPrimary = false, SortOrder = 1 },
                new SessionGroups { EventSessionGroupId = primaryGroupId, IsPrimary = true, SortOrder = 2 }
            ]
        };

        var state = EventSessionFormModelMapper.PopulateUpdateRequest(
            request,
            source,
            eventId,
            BrusselsTimeZoneId);

        await Assert.That(request.Event?.EventId).IsEqualTo(eventId);
        await Assert.That(request.Title?.Value?.Value).IsEqualTo("Workshop");
        await Assert.That(request.Description?.Value?.Value).IsEqualTo("Practical work");
        await Assert.That(request.IslamicAspect).IsNotNull();
        await Assert.That(request.IslamicAspect!.Value?.Value?.ReferencePrayer).IsEqualTo((PrayerTime)2);
        await Assert.That(request.IslamicAspect.Value?.Value?.RequiresWudu).IsTrue();
        await Assert.That(state.PrimarySessionGroupId).IsEqualTo(primaryGroupId);
        await Assert.That(state.SessionDate).IsEqualTo(new DateTime(2026, 7, 3));
        await Assert.That(state.StartTime).IsEqualTo(new TimeSpan(16, 0, 0));
        await Assert.That(state.EndTime).IsEqualTo(new TimeSpan(17, 30, 0));
    }

    [Test]
    public async Task TryPrepareUpdateRequest_RejectsMismatchedSessionContext()
    {
        var request = new UpdateEventSessionDto
        {
            Title = new UpdateEventSessionTitleDto
            {
                Value = new OptionalUpdateOfstring { HasValue = true, Value = "Workshop" }
            }
        };

        var prepared = EventSessionFormModelMapper.TryPrepareUpdateRequest(
            request,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 6, 1),
            new TimeSpan(9, 0, 0),
            new TimeSpan(10, 0, 0),
            out var validationError,
            BrusselsTimeZoneId);

        await Assert.That(prepared).IsFalse();
        await Assert.That(validationError).IsEqualTo("The session context is invalid. Return to the event draft and try again.");
    }

    [Test]
    public async Task TryConvertLocalToUtc_RejectsEuropeBrusselsSpringGap()
    {
        bool converted = DateTimeHelper.TryConvertLocalToUtc(
            new DateTime(2026, 3, 29, 2, 30, 0),
            BrusselsTimeZoneId,
            existingInstant: null,
            out _,
            out string? validationError);

        await Assert.That(converted).IsFalse();
        await Assert.That(validationError).Contains("does not exist in Europe/Brussels");
    }

    [Test]
    public async Task TryConvertLocalToUtc_RejectsNewEuropeBrusselsOverlapWithoutOccurrence()
    {
        bool converted = DateTimeHelper.TryConvertLocalToUtc(
            new DateTime(2026, 10, 25, 2, 30, 0),
            BrusselsTimeZoneId,
            existingInstant: null,
            out _,
            out string? validationError);

        await Assert.That(converted).IsFalse();
        await Assert.That(validationError).Contains("occurs twice in Europe/Brussels");
    }

    [Test]
    [Arguments("2026-10-25T00:30:00+00:00")]
    [Arguments("2026-10-25T01:30:00+00:00")]
    public async Task TryConvertLocalToUtc_PreservesEitherPersistedEuropeBrusselsOverlapOccurrence(
        string persistedUtcText)
    {
        DateTimeOffset persistedUtc = DateTimeOffset.Parse(persistedUtcText);

        bool converted = DateTimeHelper.TryConvertLocalToUtc(
            new DateTime(2026, 10, 25, 2, 30, 0),
            BrusselsTimeZoneId,
            persistedUtc,
            out DateTimeOffset actualUtc,
            out string? validationError);

        await Assert.That(converted).IsTrue();
        await Assert.That(validationError).IsNull();
        await Assert.That(actualUtc).IsEqualTo(persistedUtc);
    }
}
