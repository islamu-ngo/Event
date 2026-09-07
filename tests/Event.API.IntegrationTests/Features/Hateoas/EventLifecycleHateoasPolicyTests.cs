using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Explore.Domain.Services.Lifecycle;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class EventLifecycleHateoasPolicyTests
{
    [Test]
    public async Task EventDetailLinks_PointSessionCreationAffordancesToDraftLifecycleRoute()
    {
        var dto = CreateEventDto(EventStatusEnum.Draft);
        var links = new EventDetailLinkPolicy().GetLinks(dto, user: null).ToArray();

        var addSession = links.Single(link => link.Rel == LinkRelations.AddSession);
        var createDraft = links.Single(link => link.Rel == LinkRelations.CreateSessionDraft);

        await Assert.That(addSession.RouteName).IsEqualTo(RouteNames.CreateDraftEventSession);
        await Assert.That(createDraft.RouteName).IsEqualTo(RouteNames.CreateDraftEventSession);
        await Assert.That(createDraft.Method).IsEqualTo("POST");
        await Assert.That(createDraft.RequiresAuth).IsTrue();
        await Assert.That(createDraft.PermissionAction).IsEqualTo(AuthorizationActions.Create);
        await Assert.That(createDraft.PermissionResourceKind).IsEqualTo(ResourceKinds.EventSession);
    }

    [Test]
    public async Task EventDetailLinks_MatchDomainLifecycleRulesForEveryStatusAndAction()
    {
        var failures = new List<string>();
        var actions = new[]
        {
            (LinkRelations.Publish, RouteNames.PublishEvent, EventStatusEnum.Published, AuthorizationActions.Update),
            (LinkRelations.Cancel, RouteNames.CancelEvent, EventStatusEnum.Cancelled, AuthorizationActions.Update),
            (LinkRelations.Archive, RouteNames.ArchiveEvent, EventStatusEnum.Archived, AuthorizationActions.Update),
            (LinkRelations.ModerateLight, RouteNames.ModerateEventLight, EventStatusEnum.Moderated, AuthorizationActions.Events.ModerateLight)
        };

        foreach (var current in Enum.GetValues<EventStatusEnum>())
        {
            var links = new EventDetailLinkPolicy().GetLinks(CreateEventDto(current), user: null).ToArray();
            foreach (var (relation, routeName, target, permission) in actions)
            {
                var link = links.SingleOrDefault(candidate => candidate.Rel == relation);
                var expected = current != target && EventLifecycleRules.CanTransition(current, target);
                RecordParityFailure(failures, $"Event {current} -> {target}", link is not null, expected);
                RecordAuthorizationFailure(failures, $"Event {current} {relation}", link, routeName, permission, ResourceKinds.Event);
            }

            var readiness = links.SingleOrDefault(candidate => candidate.Rel == LinkRelations.PublishReadiness);
            var publishExpected = current != EventStatusEnum.Published
                && EventLifecycleRules.CanTransition(current, EventStatusEnum.Published);
            RecordParityFailure(failures, $"Event {current} publish readiness", readiness is not null, publishExpected);
            RecordAuthorizationFailure(failures, $"Event {current} publish readiness", readiness, RouteNames.GetEventPublishReadiness, AuthorizationActions.Update, ResourceKinds.Event);
        }

        await Assert.That(failures).IsEmpty().Because(string.Join(Environment.NewLine, failures));
    }

    [Test]
    public async Task EventDetailLinks_UseRestorationRuleAndPreserveModerationRecordEligibility()
    {
        var failures = new List<string>();

        foreach (var status in Enum.GetValues<EventStatusEnum>())
            foreach (var isEligible in new[] { false, true })
            {
                var dto = CreateEventDto(status);
                dto.IsUnmoderationEligible = isEligible;
                var links = new EventDetailLinkPolicy().GetLinks(dto, user: null).ToArray();
                var restore = links.SingleOrDefault(candidate => candidate.Rel == LinkRelations.Unmoderate);
                var restoreExpected = EventLifecycleRules.CanRestoreAfterLightModeration(status) && isEligible;
                RecordParityFailure(failures, $"Event {status} restore eligible={isEligible}", restore is not null, restoreExpected);
                RecordAuthorizationFailure(failures, $"Event {status} restore", restore, RouteNames.UnmoderateEvent, AuthorizationActions.Events.Unmoderate, ResourceKinds.Event);

                var heavy = links.SingleOrDefault(candidate => candidate.Rel == LinkRelations.ModerateHeavy);
                var heavyExpected = status != EventStatusEnum.Moderated || isEligible;
                RecordParityFailure(failures, $"Event {status} heavy moderation eligible={isEligible}", heavy is not null, heavyExpected);
                RecordAuthorizationFailure(failures, $"Event {status} heavy moderation", heavy, RouteNames.ModerateEventHeavy, AuthorizationActions.Events.ModerateHeavy, ResourceKinds.Event);
            }

        await Assert.That(failures).IsEmpty().Because(string.Join(Environment.NewLine, failures));
    }

    [Test]
    public async Task EventSessionDetailAndCollectionLinks_MatchDomainRulesForEveryLifecycleInput()
    {
        var failures = new List<string>();
        var start = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var schedules = new[]
        {
            (Start: (DateTimeOffset?)null, End: (DateTimeOffset?)null, Type: SessionEndTimeType.Fixed),
            (Start: (DateTimeOffset?)start, End: (DateTimeOffset?)start.AddHours(1), Type: SessionEndTimeType.Fixed),
            (Start: (DateTimeOffset?)start, End: (DateTimeOffset?)null, Type: SessionEndTimeType.Fixed),
            (Start: (DateTimeOffset?)start, End: (DateTimeOffset?)start, Type: SessionEndTimeType.Fixed),
            (Start: (DateTimeOffset?)start, End: (DateTimeOffset?)null, Type: SessionEndTimeType.OpenEnded),
            (Start: (DateTimeOffset?)start, End: (DateTimeOffset?)start.AddHours(1), Type: SessionEndTimeType.OpenEnded),
            (Start: (DateTimeOffset?)start, End: (DateTimeOffset?)null, Type: SessionEndTimeType.RelativeToPrayer),
            (Start: (DateTimeOffset?)start, End: (DateTimeOffset?)start.AddHours(1), Type: SessionEndTimeType.RelativeToPrayer),
            (Start: (DateTimeOffset?)start, End: (DateTimeOffset?)start, Type: SessionEndTimeType.RelativeToPrayer)
        };

        foreach (var current in Enum.GetValues<EventSessionStatusEnum>())
            foreach (var parent in Enum.GetValues<EventStatusEnum>())
                foreach (var schedule in schedules)
                {
                    var detailDto = CreateSessionDto(current, parent, schedule.Start, schedule.End, schedule.Type);
                    var listDto = CreateSessionListDto(current, parent, schedule.Start, schedule.End, schedule.Type);
                    var detailLinks = new EventSessionDetailLinkPolicy().GetLinks(detailDto, user: null).ToArray();
                    var listLinks = new EventSessionCollectionLinkPolicy().GetItemLinks(listDto, user: null).ToArray();

                    RecordSessionParityFailures(failures, "detail", current, parent, schedule, detailLinks);
                    RecordSessionParityFailures(failures, "list", current, parent, schedule, listLinks);
                }

        await Assert.That(failures).IsEmpty().Because(string.Join(Environment.NewLine, failures));
    }

    private static EventDto CreateEventDto(EventStatusEnum status) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Lifecycle Event",
        ActorId = Guid.NewGuid(),
        ActorDisplayName = "Organizer",
        ActorTypeFullName = "User",
        EventStatusId = (int)status,
        EventStatusFullName = status.ToString(),
        EventStatusMasterCode = status.ToString(),
        VisibilityTypeFullName = "Public",
        VisibilityTypeMasterCode = "public",
        EventFormatFullName = "In Person",
        EventFormatMasterCode = "in_person",
        TenantId = Guid.NewGuid()
    };

    private static EventSessionDto CreateSessionDto(
        EventSessionStatusEnum status,
        EventStatusEnum parentStatus,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        SessionEndTimeType endTimeType) => new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EventTitle = "Lifecycle Event",
            Title = "Lifecycle Session",
            EventSessionStatusId = (int)status,
            ParentEventStatusId = (int)parentStatus,
            StartTime = startTime,
            EndTime = endTime,
            EndTimeType = endTimeType,
            IsScheduled = startTime is not null,
            TenantId = Guid.NewGuid()
        };

    private static EventSessionListDto CreateSessionListDto(
        EventSessionStatusEnum status,
        EventStatusEnum parentStatus,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        SessionEndTimeType endTimeType) => new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EventTitle = "Lifecycle Event",
            Title = "Lifecycle Session",
            EventSessionStatusId = (int)status,
            ParentEventStatusId = (int)parentStatus,
            StartTime = startTime,
            EndTime = endTime,
            EndTimeType = endTimeType,
            IsScheduled = startTime is not null,
            TenantId = Guid.NewGuid()
        };

    private static void RecordSessionParityFailures(
        List<string> failures,
        string surface,
        EventSessionStatusEnum current,
        EventStatusEnum parent,
        (DateTimeOffset? Start, DateTimeOffset? End, SessionEndTimeType Type) schedule,
        IReadOnlyCollection<LinkDefinition> links)
    {
        var scenario = $"Session {surface} current={current} parent={parent} start={schedule.Start is not null} end={schedule.End is not null} type={schedule.Type}";
        var actions = new[]
        {
            (LinkRelations.Schedule, RouteNames.ScheduleEventSession, EventSessionLifecycleRules.CanSchedule(current)),
            (LinkRelations.Publish, RouteNames.PublishEventSession, EventSessionLifecycleRules.CanPublish(current, parent, schedule.Start, schedule.End, schedule.Type)),
            (LinkRelations.Cancel, RouteNames.CancelEventSession, EventSessionLifecycleRules.CanCancel(current, parent)),
            (LinkRelations.Complete, RouteNames.CompleteEventSession, EventSessionLifecycleRules.CanComplete(current, parent)),
            (LinkRelations.Archive, RouteNames.ArchiveEventSession, EventSessionLifecycleRules.CanArchive(current, parent))
        };

        foreach (var (relation, routeName, expected) in actions)
        {
            var link = links.SingleOrDefault(candidate => candidate.Rel == relation);
            RecordParityFailure(failures, $"{scenario} action={relation}", link is not null, expected);
            RecordAuthorizationFailure(failures, $"{scenario} action={relation}", link, routeName, AuthorizationActions.Update, ResourceKinds.EventSession);
        }
    }

    private static void RecordParityFailure(List<string> failures, string scenario, bool actual, bool expected)
    {
        if (actual != expected)
        {
            failures.Add($"{scenario}: HAL={actual}, Domain={expected}");
        }
    }

    private static void RecordAuthorizationFailure(
        List<string> failures,
        string scenario,
        LinkDefinition? link,
        string routeName,
        string permission,
        string resourceKind)
    {
        if (link is null)
        {
            return;
        }

        if (!link.RequiresAuth || link.RouteName != routeName || link.PermissionAction != permission || link.PermissionResourceKind != resourceKind)
        {
            failures.Add($"{scenario}: authorization or route metadata drifted");
        }
    }
}
