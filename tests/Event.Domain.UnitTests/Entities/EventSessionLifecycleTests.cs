using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventSessionLifecycleTests
{
    private static readonly DateTime OccurredAt = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NonUtcOccurredAt = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Local);
    private static readonly DateTimeOffset Start = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(2);
    private readonly EventScheduleProjectionCalculator _calculator = new();

    [Test]
    public async Task SemanticTransitions_UpdateOnlyStatusAndUpdatedAtWhilePreservingConcurrencyStamp()
    {
        foreach (var scenario in SuccessfulTransitions())
        {
            var session = CreateSession(scenario.From, scheduled: scenario.From != EventSessionStatusEnum.Completed);
            var stamp = session.ConcurrencyStamp;
            var title = session.Title;

            bool changed = scenario.Invoke(session);

            await Assert.That(changed).IsTrue();
            await Assert.That(session.EventSessionStatusId).IsEqualTo((int)scenario.To);
            await Assert.That(session.UpdatedAt).IsEqualTo(OccurredAt);
            await Assert.That(session.ConcurrencyStamp).IsEqualTo(stamp);
            await Assert.That(session.Title).IsEqualTo(title);
        }
    }

    [Test]
    public async Task SameTargetTransitions_AreNoOpsBeforeParentEligibility()
    {
        var cases = new (EventSessionStatusEnum Status, Func<EventSession, bool> Invoke)[]
        {
            (EventSessionStatusEnum.Published, session => session.Publish(EventStatusEnum.Draft, OccurredAt)),
            (EventSessionStatusEnum.Cancelled, session => session.Cancel(EventStatusEnum.Moderated, OccurredAt)),
            (EventSessionStatusEnum.Completed, session => session.Complete(EventStatusEnum.Draft, OccurredAt)),
            (EventSessionStatusEnum.Archived, session => session.Archive(EventStatusEnum.Archived, OccurredAt)),
            (EventSessionStatusEnum.Moderated, session => session.ApplyParentModeration(OccurredAt)),
            (EventSessionStatusEnum.Submitted, session => session.SynchronizeFederatedLifecycle(EventSessionStatusEnum.Submitted, OccurredAt))
        };

        foreach (var item in cases)
        {
            var session = CreateSession(item.Status, scheduled: true);
            var before = Snapshot(session);

            await Assert.That(item.Invoke(session)).IsFalse();
            await Assert.That(Snapshot(session)).IsEqualTo(before);
        }
    }

    [Test]
    public async Task InvalidTransitions_ThrowWithoutPartialMutation()
    {
        var invalid = new (EventSessionStatusEnum Status, Func<EventSession, bool> Invoke)[]
        {
            (EventSessionStatusEnum.Rejected, session => session.Publish(EventStatusEnum.Published, OccurredAt)),
            (EventSessionStatusEnum.Archived, session => session.Cancel(EventStatusEnum.Published, OccurredAt)),
            (EventSessionStatusEnum.Draft, session => session.Complete(EventStatusEnum.Published, OccurredAt)),
            (EventSessionStatusEnum.Published, session => session.Archive(EventStatusEnum.Published, OccurredAt)),
            (EventSessionStatusEnum.Draft, session => session.Publish(EventStatusEnum.Published, OccurredAt))
        };

        foreach (var item in invalid)
        {
            var session = CreateSession(item.Status, scheduled: item.Status != EventSessionStatusEnum.Draft);
            var before = Snapshot(session);

            await Assert.That(() => item.Invoke(session)).Throws<InvalidOperationException>();
            await Assert.That(Snapshot(session)).IsEqualTo(before);
        }
    }

    [Test]
    public async Task Publish_RequiresEndTimeShapeMatchingEndTimeType()
    {
        var fixedMissingEnd = CreateSession(EventSessionStatusEnum.Draft, scheduled: false);
        fixedMissingEnd.StartTime = Start;
        fixedMissingEnd.EndTimeType = SessionEndTimeType.Fixed;
        var fixedMissingEndBefore = Snapshot(fixedMissingEnd);

        await Assert.That(() => fixedMissingEnd.Publish(EventStatusEnum.Published, OccurredAt)).Throws<InvalidOperationException>();
        await Assert.That(Snapshot(fixedMissingEnd)).IsEqualTo(fixedMissingEndBefore);

        var openEnded = CreateSession(EventSessionStatusEnum.Draft, scheduled: false);
        openEnded.ScheduleOpenEnded(Start, "UTC", _calculator);

        await Assert.That(openEnded.Publish(EventStatusEnum.Published, OccurredAt)).IsTrue();

        var fixedWithEnd = CreateSession(EventSessionStatusEnum.Draft, scheduled: true);
        fixedWithEnd.EndTimeType = SessionEndTimeType.Fixed;

        await Assert.That(fixedWithEnd.Publish(EventStatusEnum.Published, OccurredAt)).IsTrue();

        var relativeToPrayer = CreateSession(EventSessionStatusEnum.Draft, scheduled: false);
        relativeToPrayer.ScheduleRelativeToPrayer(Start, "UTC", _calculator);

        await Assert.That(relativeToPrayer.Publish(EventStatusEnum.Published, OccurredAt)).IsTrue();
    }

    [Test]
    public async Task RealLifecycleTransitions_RejectNonUtcTimestampsWithoutMutation()
    {
        var cases = new (EventSessionStatusEnum Status, Func<EventSession, bool> Invoke)[]
        {
            (EventSessionStatusEnum.Draft, session => session.Publish(EventStatusEnum.Published, NonUtcOccurredAt)),
            (EventSessionStatusEnum.Draft, session => session.Cancel(EventStatusEnum.Published, NonUtcOccurredAt)),
            (EventSessionStatusEnum.Published, session => session.Complete(EventStatusEnum.Published, NonUtcOccurredAt)),
            (EventSessionStatusEnum.Completed, session => session.Archive(EventStatusEnum.Published, NonUtcOccurredAt)),
            (EventSessionStatusEnum.Draft, session => session.ApplyParentModeration(NonUtcOccurredAt)),
            (EventSessionStatusEnum.Draft, session => session.SynchronizeFederatedLifecycle(EventSessionStatusEnum.Approved, NonUtcOccurredAt))
        };

        foreach (var item in cases)
        {
            var session = CreateSession(item.Status, scheduled: true);
            var before = Snapshot(session);

            await Assert.That(() => item.Invoke(session)).Throws<ArgumentException>();
            await Assert.That(Snapshot(session)).IsEqualTo(before);
        }
    }

    [Test]
    public async Task ModerationAndFederationSeams_ValidateAndMutateExplicitly()
    {
        var moderated = CreateSession(EventSessionStatusEnum.Draft, scheduled: false);
        await Assert.That(moderated.ApplyParentModeration(OccurredAt)).IsTrue();
        await Assert.That(moderated.EventSessionStatusId).IsEqualTo((int)EventSessionStatusEnum.Moderated);
        await Assert.That(moderated.UpdatedAt).IsEqualTo(OccurredAt);

        var federated = CreateSession(EventSessionStatusEnum.Draft, scheduled: false);
        await Assert.That(federated.SynchronizeFederatedLifecycle(EventSessionStatusEnum.Rejected, OccurredAt)).IsTrue();
        await Assert.That(federated.EventSessionStatusId).IsEqualTo((int)EventSessionStatusEnum.Rejected);
        await Assert.That(federated.UpdatedAt).IsEqualTo(OccurredAt);

        var before = Snapshot(federated);
        await Assert.That(() => federated.SynchronizeFederatedLifecycle((EventSessionStatusEnum)999, OccurredAt)).Throws<ArgumentException>();
        await Assert.That(Snapshot(federated)).IsEqualTo(before);
    }

    [Test]
    public async Task Reschedule_RejectsDisallowedStatusOrRangeWithoutPartialScheduleMutation()
    {
        var archived = CreateSession(EventSessionStatusEnum.Archived, scheduled: true);
        archived.ReprojectLocalTimes("Europe/Brussels", _calculator);
        var archivedBefore = ScheduleSnapshot(archived);

        await Assert.That(() => archived.Reschedule(UtcInstantRange.Create(Start.AddDays(1), End.AddDays(1)), "Asia/Tokyo", _calculator))
            .Throws<InvalidOperationException>();
        await Assert.That(ScheduleSnapshot(archived)).IsEqualTo(archivedBefore);

        var draft = CreateSession(EventSessionStatusEnum.Draft, scheduled: true);
        draft.Reschedule(UtcInstantRange.Create(Start, End), "Europe/Brussels", _calculator);
        var draftBefore = ScheduleSnapshot(draft);

        await Assert.That(() => UtcInstantRange.Create(Start.AddDays(1), Start.AddDays(1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(ScheduleSnapshot(draft)).IsEqualTo(draftBefore);
    }

    private static IEnumerable<(EventSessionStatusEnum From, EventSessionStatusEnum To, Func<EventSession, bool> Invoke)> SuccessfulTransitions()
    {
        yield return (EventSessionStatusEnum.Draft, EventSessionStatusEnum.Published, session => session.Publish(EventStatusEnum.Published, OccurredAt));
        yield return (EventSessionStatusEnum.Published, EventSessionStatusEnum.Cancelled, session => session.Cancel(EventStatusEnum.Published, OccurredAt));
        yield return (EventSessionStatusEnum.Published, EventSessionStatusEnum.Completed, session => session.Complete(EventStatusEnum.Published, OccurredAt));
        yield return (EventSessionStatusEnum.Completed, EventSessionStatusEnum.Archived, session => session.Archive(EventStatusEnum.Published, OccurredAt));
    }

    private static EventSession CreateSession(EventSessionStatusEnum status, bool scheduled)
    {
        return new EventSession(status)
        {
            Event = CreateEvent(),
            Tenant = CreateTenant(),
            Title = "Session",
            StartTime = scheduled ? Start : null,
            EndTime = scheduled ? End : null,
            UpdatedAt = OccurredAt.AddDays(-1),
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    private static global::Explore.Domain.Event CreateEvent()
    {
        return new global::Explore.Domain.Event
        {
            Id = Guid.CreateVersion7(),
            Title = "Event",
            Actor = new Actor
            {
                Pii = new ActorPii { DisplayName = "Actor" },
                ActorType = new ActorType { FullName = "User", MasterCode = "USER" }
            },
            Tenant = CreateTenant(),
            VisibilityType = new VisibilityType { MasterCode = "PUBLIC", FullName = "Public" },
            EventStatus = new EventStatus { MasterCode = "DRAFT", FullName = "Draft" },
            EventFormat = new EventFormat { MasterCode = "ONLINE", FullName = "Online" }
        };
    }

    private static Tenant CreateTenant()
    {
        return new Tenant
        {
            FullName = "Tenant",
            Slug = "tenant",
            TenantStatusId = 2,
            TenantStatus = new TenantStatus { Id = 2, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true }
        };
    }

    private static (int StatusId, DateTime? UpdatedAt, Guid Stamp, string? Title) Snapshot(EventSession session) =>
        (session.EventSessionStatusId, session.UpdatedAt, session.ConcurrencyStamp, session.Title);

    private static (DateTimeOffset? Start, DateTimeOffset? End, DateOnly? StartDate, DateOnly? EndDate, TimeOnly? StartTime, TimeOnly? EndTime, int? StartMinute, int? EndMinute) ScheduleSnapshot(EventSession session) =>
        (session.StartTime, session.EndTime, session.LocalStartDate, session.LocalEndDate, session.LocalStartTime, session.LocalEndTime, session.LocalStartMinuteOfDay, session.LocalEndMinuteOfDay);
}
