using Explore.Application.Services.Lifecycle;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Lifecycle;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Services;

public sealed class EventLifecycleReadinessEvaluatorTests
{
    private readonly EventLifecycleReadinessEvaluator _evaluator = new();

    [Test]
    public async Task EvaluateEventPublishWhenScheduleIsMissingReturnsMachineReadableErrors()
    {
        var eventEntity = CreateReadyEvent();
        eventEntity.FirstSessionStartUtc = null;

        var result = _evaluator.Evaluate(eventEntity, ValidationProfile.EventPublish, CreateEventPublishPolicy());

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("schedule_session_required");
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("schedule_first_start_required");
        await Assert.That(result.Errors.Single(error => error.Code == "schedule_session_required").FieldPath).IsEqualTo("schedule.sessions");
        await Assert.That(result.Errors.All(error => error.Profile == ValidationProfile.EventPublish)).IsTrue();
    }

    [Test]
    public async Task EvaluateCommunityPublishWhenEventIsModeratedReturnsHardInvariantError()
    {
        var eventEntity = CreateReadyEvent(EventStatusEnum.Moderated);

        LifecycleReadinessResult result = _evaluator.Evaluate(
            eventEntity,
            ValidationProfile.EventPublishCommunityLexicon,
            CreateCommunityPublishPolicy());

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("event_moderated");
        await Assert.That(result.Errors.Single(error => error.Code == "event_moderated").Source)
            .IsEqualTo(ReadinessErrorSource.HardInvariant);
    }

    [Test]
    public async Task EvaluateSessionDraftCreateWhenMinimalDraftFieldsExistReturnsReady()
    {
        var session = CreateDraftSession();

        var result = _evaluator.Evaluate(session, parentEvent: null, ValidationProfile.SessionDraftCreate, CreateSessionDraftPolicy());

        await Assert.That(result.IsReady).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async Task EvaluateSessionScheduleWhenEndIsBeforeStartReturnsRangeError()
    {
        var session = CreateDraftSession();
        session.StartTime = DateTimeOffset.UtcNow.AddHours(2);
        session.EndTime = DateTimeOffset.UtcNow.AddHours(1);

        var result = _evaluator.Evaluate(session, parentEvent: null, ValidationProfile.SessionSchedule, CreateSessionSchedulePolicy());

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("session_schedule_range_invalid");
        await Assert.That(result.Errors.Single(error => error.Code == "session_schedule_range_invalid").Source).IsEqualTo(ReadinessErrorSource.DomainRule);
    }

    [Test]
    public async Task EvaluateSessionPublishWhenParentEventIsDraftReturnsCompatibilityError()
    {
        var session = CreateDraftSession();
        session.StartTime = DateTimeOffset.UtcNow.AddDays(1);
        session.EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(1);
        var parentEvent = CreateReadyEvent(EventStatusEnum.Draft);

        var result = _evaluator.Evaluate(session, parentEvent, ValidationProfile.SessionPublish, CreateSessionPublishPolicy());

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("session_parent_event_not_published");
        await Assert.That(result.Errors.Single(error => error.Code == "session_parent_event_not_published").FieldPath).IsEqualTo("event.status");
    }

    [Test]
    public async Task EvaluateSessionPublishWhenSessionIsRejectedReturnsHardInvariantError()
    {
        var session = CreateDraftSession(EventSessionStatusEnum.Rejected);
        session.StartTime = DateTimeOffset.UtcNow.AddDays(1);
        session.EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(1);
        var parentEvent = CreateReadyEvent(EventStatusEnum.Published);

        var result = _evaluator.Evaluate(session, parentEvent, ValidationProfile.SessionPublish, CreateSessionPublishPolicy());

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("session_rejected");
        await Assert.That(result.Errors.Single(error => error.Code == "session_rejected").Source).IsEqualTo(ReadinessErrorSource.HardInvariant);
    }

    [Test]
    public async Task EvaluateEventPublishMatchesDomainRuleForCompletedAndPublishedStatuses()
    {
        var completedEvent = CreateReadyEvent(EventStatusEnum.Completed);
        var publishedEvent = CreateReadyEvent(EventStatusEnum.Published);

        LifecycleReadinessResult completed = _evaluator.Evaluate(completedEvent, ValidationProfile.EventPublish, CreateEventPublishPolicy());
        LifecycleReadinessResult published = _evaluator.Evaluate(publishedEvent, ValidationProfile.EventPublish, CreateEventPublishPolicy());

        await Assert.That(completed.IsReady)
            .IsEqualTo(EventLifecycleRules.CanTransition(EventStatusEnum.Completed, EventStatusEnum.Published));
        await Assert.That(completed.Errors.Select(error => error.Code)).Contains("event_completed");
        await Assert.That(published.IsReady)
            .IsEqualTo(EventLifecycleRules.CanTransition(EventStatusEnum.Published, EventStatusEnum.Published));
        await Assert.That(published.Errors).IsEmpty();
    }

    [Test]
    public async Task EvaluateEventPublicationSessionUsesPublishedTargetParent()
    {
        var session = CreateDraftSession();
        session.StartTime = DateTimeOffset.UtcNow.AddDays(1);
        session.EndTime = session.StartTime.Value.AddHours(1);
        var draftParent = CreateReadyEvent();
        var policy = CreateSessionPublishPolicy() with { Profile = ValidationProfile.EventPublish };

        LifecycleReadinessResult result = _evaluator.Evaluate(session, draftParent, ValidationProfile.EventPublish, policy);

        await Assert.That(result.IsReady).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async Task EvaluateSessionPublishWhenOpenEndedScheduleHasEndReturnsDomainRuleError()
    {
        var session = CreateDraftSession();
        session.StartTime = DateTimeOffset.UtcNow.AddDays(1);
        session.EndTime = session.StartTime.Value.AddHours(1);
        session.EndTimeType = SessionEndTimeType.OpenEnded;
        var publishedParent = CreateReadyEvent(EventStatusEnum.Published);

        LifecycleReadinessResult result = _evaluator.Evaluate(session, publishedParent, ValidationProfile.SessionPublish, CreateSessionPublishPolicy());

        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(
            session.StartTime,
            session.EndTime,
            session.EndTimeType)).IsFalse();
        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("session_schedule_range_invalid");
    }

    [Test]
    public async Task EvaluateSessionScheduleReadinessMatchesDomainSchedulePredicate()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(1);
        (DateTimeOffset? Start, DateTimeOffset? End, SessionEndTimeType EndType)[] schedules =
        [
            (null, start.AddHours(1), SessionEndTimeType.Fixed),
            (start, null, SessionEndTimeType.Fixed),
            (start, start, SessionEndTimeType.Fixed),
            (start, start.AddHours(1), SessionEndTimeType.Fixed),
            (start, null, SessionEndTimeType.OpenEnded),
            (start, start.AddHours(1), SessionEndTimeType.OpenEnded),
            (start, null, SessionEndTimeType.RelativeToPrayer),
            (start, start, SessionEndTimeType.RelativeToPrayer),
            (start, start.AddHours(1), SessionEndTimeType.RelativeToPrayer),
            (start, start.AddHours(1), (SessionEndTimeType)999)
        ];

        foreach ((DateTimeOffset? scheduleStart, DateTimeOffset? scheduleEnd, SessionEndTimeType endType) in schedules)
        {
            var session = CreateDraftSession();
            session.StartTime = scheduleStart;
            session.EndTime = scheduleEnd;
            session.EndTimeType = endType;

            LifecycleReadinessResult result = _evaluator.Evaluate(
                session,
                parentEvent: null,
                ValidationProfile.SessionSchedule,
                CreateSessionSchedulePolicy());

            await Assert.That(result.IsReady).IsEqualTo(
                EventSessionLifecycleRules.HasPublishableSchedule(scheduleStart, scheduleEnd, endType));
        }
    }

    [Test]
    public async Task EvaluateSessionPublishParentReadinessMatchesDomainCompatibilityPredicate()
    {
        foreach (EventStatusEnum parentStatus in Enum.GetValues<EventStatusEnum>())
        {
            var session = CreateDraftSession();
            session.StartTime = DateTimeOffset.UtcNow.AddDays(1);
            session.EndTime = session.StartTime.Value.AddHours(1);
            var parentEvent = CreateReadyEvent(parentStatus);

            LifecycleReadinessResult result = _evaluator.Evaluate(
                session,
                parentEvent,
                ValidationProfile.SessionPublish,
                CreateSessionPublishPolicy());
            bool expected = EventSessionLifecycleRules.IsPublishParentCompatible(parentStatus);

            await Assert.That(result.IsReady).IsEqualTo(expected);
            await Assert.That(result.Errors.Any(error => error.Code == "session_parent_event_not_published"))
                .IsEqualTo(!expected);
        }
    }

    [Test]
    public async Task EvaluateEventPublishRetainsDynamicRequiredFieldDiagnostics()
    {
        var eventEntity = CreateReadyEvent();
        EventLifecyclePolicy policy = CreateEventPublishPolicy() with
        {
            RequiredEventFields = new HashSet<Enum>(CreateEventPublishPolicy().RequiredEventFields)
            {
                EventFieldKey.Description
            },
            Source = "tenant-test-policy"
        };

        LifecycleReadinessResult result = _evaluator.Evaluate(eventEntity, ValidationProfile.EventPublish, policy);

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("description_required");
        await Assert.That(result.Errors.Single(error => error.Code == "description_required").Source)
            .IsEqualTo(ReadinessErrorSource.CommandProfile);
        await Assert.That(policy.Source).IsEqualTo("tenant-test-policy");
    }

    [Test]
    public async Task EvaluateEventPublishWhenScheduleLastEndIsMissingReturnsBlocker()
    {
        var eventEntity = CreateReadyEvent();
        eventEntity.LastSessionEndUtc = null;

        LifecycleReadinessResult result = _evaluator.Evaluate(
            eventEntity,
            ValidationProfile.EventPublish,
            CreateScheduleLastEndPolicy());

        await Assert.That(result.IsReady).IsFalse();
        LifecycleReadinessError error = result.Errors.Single();
        await Assert.That(error.Code).IsEqualTo("schedule_last_end_required");
        await Assert.That(error.FieldKey).IsEqualTo(EventFieldKey.ScheduleLastEnd);
        await Assert.That(error.FieldPath).IsEqualTo("schedule.last_end");
    }

    [Test]
    public async Task EvaluateEventPublishWhenScheduleLastEndExistsReturnsReady()
    {
        var eventEntity = CreateReadyEvent();
        eventEntity.LastSessionStartUtc = null;
        eventEntity.LastSessionEndUtc = DateTimeOffset.UtcNow.AddDays(1).AddHours(2);

        LifecycleReadinessResult result = _evaluator.Evaluate(
            eventEntity,
            ValidationProfile.EventPublish,
            CreateScheduleLastEndPolicy());

        await Assert.That(result.IsReady).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    private static EventLifecyclePolicy CreateEventPublishPolicy() => new()
    {
        Profile = ValidationProfile.EventPublish,
        RequiredEventFields = new HashSet<Enum>
        {
            EventFieldKey.Title,
            EventFieldKey.Tenant,
            EventFieldKey.Owner,
            EventFieldKey.Status,
            EventFieldKey.Visibility,
            EventFieldKey.Format,
            EventFieldKey.ScheduleSessions,
            EventFieldKey.ScheduleFirstStart
        },
        RequiredSessionFields = new HashSet<Enum>()
    };

    private static EventLifecyclePolicy CreateCommunityPublishPolicy() => new()
    {
        Profile = ValidationProfile.EventPublishCommunityLexicon,
        RequiredEventFields = new HashSet<Enum>
        {
            EventFieldKey.Title,
            EventFieldKey.Tenant,
            EventFieldKey.Owner,
            EventFieldKey.Status
        },
        RequiredSessionFields = new HashSet<Enum>()
    };

    private static EventLifecyclePolicy CreateScheduleLastEndPolicy() => new()
    {
        Profile = ValidationProfile.EventPublish,
        RequiredEventFields = new HashSet<Enum> { EventFieldKey.ScheduleLastEnd },
        RequiredSessionFields = new HashSet<Enum>()
    };

    private static EventLifecyclePolicy CreateSessionDraftPolicy() => new()
    {
        Profile = ValidationProfile.SessionDraftCreate,
        RequiredEventFields = new HashSet<Enum>(),
        RequiredSessionFields = new HashSet<Enum>
        {
            EventSessionFieldKey.ParentEvent,
            EventSessionFieldKey.Tenant,
            EventSessionFieldKey.Status,
            EventSessionFieldKey.Title
        }
    };

    private static EventLifecyclePolicy CreateSessionSchedulePolicy() => new()
    {
        Profile = ValidationProfile.SessionSchedule,
        RequiredEventFields = new HashSet<Enum>(),
        RequiredSessionFields = new HashSet<Enum>
        {
            EventSessionFieldKey.ParentEvent,
            EventSessionFieldKey.Tenant,
            EventSessionFieldKey.Status,
            EventSessionFieldKey.ScheduleStart,
            EventSessionFieldKey.ScheduleEnd
        }
    };

    private static EventLifecyclePolicy CreateSessionPublishPolicy() => new()
    {
        Profile = ValidationProfile.SessionPublish,
        RequiredEventFields = new HashSet<Enum>(),
        RequiredSessionFields = new HashSet<Enum>
        {
            EventSessionFieldKey.ParentEvent,
            EventSessionFieldKey.Tenant,
            EventSessionFieldKey.Status,
            EventSessionFieldKey.Title,
            EventSessionFieldKey.ScheduleStart,
            EventSessionFieldKey.ScheduleEnd,
            EventSessionFieldKey.ParentEventCompatibility
        }
    };

    private static Explore.Domain.Event CreateReadyEvent(EventStatusEnum status = EventStatusEnum.Draft) => new(status)
    {
        Id = Guid.NewGuid(),
        Title = "Ready Event",
        ActorId = Guid.NewGuid(),
        Actor = null!,
        TenantId = Guid.NewGuid(),
        Tenant = null!,
        VisibilityTypeId = 1,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormatId = 1,
        EventFormat = null!,
        FirstSessionStartUtc = DateTimeOffset.UtcNow.AddDays(1),
        LastSessionStartUtc = DateTimeOffset.UtcNow.AddDays(1).AddHours(1)
    };

    private static EventSession CreateDraftSession(EventSessionStatusEnum status = EventSessionStatusEnum.Draft) => new(status)
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        Event = null!,
        TenantId = Guid.NewGuid(),
        Tenant = null!,
        Title = "Draft session"
    };
}
