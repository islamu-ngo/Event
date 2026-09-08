using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Handlers.Queries;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Services.Lifecycle;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Events.Queries;

public class GetEventPublishReadinessRequestHandlerTests
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventLocationRepository _eventLocationRepository;
    private readonly IEventLifecyclePolicyProvider _policyProvider;
    private readonly GetEventPublishReadinessRequestHandler _handler;

    public GetEventPublishReadinessRequestHandlerTests()
    {
        _eventRepository = Substitute.For<IEventRepository>();
        _eventLocationRepository = Substitute.For<IEventLocationRepository>();
        _eventLocationRepository
            .GetByEventIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<EventLocation>().AsReadOnly());
        _policyProvider = Substitute.For<IEventLifecyclePolicyProvider>();
        _policyProvider
            .GetEffectivePolicyAsync(Arg.Any<Guid?>(), ValidationProfile.EventPublish, Arg.Any<CancellationToken>())
            .Returns(CreateEventPublishPolicy());

        _handler = new GetEventPublishReadinessRequestHandler(
            _eventRepository,
            _eventLocationRepository,
            _policyProvider,
            new EventLifecycleReadinessEvaluator());
    }

    [Test]
    public async Task Handle_WhenEventIsReady_ReturnsReadyResult()
    {
        var @event = CreateReadyEvent();
        _eventRepository.GetById(@event.Id).Returns(@event);

        var result = await _handler.Handle(new GetEventPublishReadinessRequest { Id = @event.Id }, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.EventId).IsEqualTo(@event.Id);
        await Assert.That(result.IsReady).IsTrue();
        await Assert.That(result.Errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Handle_WhenEventIsMissingSchedule_ReturnsMachineReadableError()
    {
        var @event = CreateReadyEvent();
        @event.FirstSessionStartUtc = null;
        _eventRepository.GetById(@event.Id).Returns(@event);

        var result = await _handler.Handle(new GetEventPublishReadinessRequest { Id = @event.Id }, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsReady).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("schedule_session_required");
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("schedule_first_start_required");
        await Assert.That(result.Errors.Single(error => error.Code == "schedule_session_required").FieldPath).IsEqualTo("schedule.sessions");
        await Assert.That(result.Errors.All(error => error.Severity == "error")).IsTrue();
    }

    [Test]
    public async Task Handle_WhenCommunityProfileEventIsModerated_ReturnsHardInvariantError()
    {
        var @event = CreateReadyEvent(EventStatusEnum.Moderated);
        _eventRepository.GetById(@event.Id).Returns(@event);
        _policyProvider
            .GetEffectivePolicyAsync(@event.TenantId, ValidationProfile.EventPublish, Arg.Any<CancellationToken>())
            .Returns(CreateCommunityPublishPolicy());

        var result = await _handler.Handle(
            new GetEventPublishReadinessRequest { Id = @event.Id },
            CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsReady).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains("event_moderated");
    }

    [Test]
    public async Task Handle_WhenEventDoesNotExist_ReturnsNull()
    {
        var eventId = Guid.NewGuid();
        _eventRepository.GetById(eventId).Returns((Explore.Domain.Event?)null);

        var result = await _handler.Handle(new GetEventPublishReadinessRequest { Id = eventId }, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    private static Explore.Domain.Event CreateReadyEvent(EventStatusEnum status = EventStatusEnum.Draft) => new(status)
    {
        Id = Guid.NewGuid(),
        Title = "Ready Event",
        ActorId = Guid.NewGuid(),
        Actor = new Actor
        {
            ActorType = new ActorType { Id = 1, FullName = "User", MasterCode = "user" },
            Pii = new ActorPii { DisplayName = "Publisher" }
        },
        TenantId = Guid.NewGuid(),
        Tenant = CreateTenant(),
        VisibilityTypeId = 1,
        VisibilityType = new VisibilityType { Id = 1, FullName = "Public", MasterCode = "public" },
        EventStatus = new EventStatus { Id = (int)EventStatusEnum.Draft, FullName = "Draft", MasterCode = "draft" },
        EventFormatId = 1,
        EventFormat = new EventFormat { Id = 1, FullName = "In person", MasterCode = "in_person" },
        FirstSessionStartUtc = DateTimeOffset.UtcNow.AddDays(1),
        LastSessionStartUtc = DateTimeOffset.UtcNow.AddDays(1).AddHours(2)
    };

    private static Tenant CreateTenant() => new()
    {
        FullName = "Test Tenant",
        Slug = "test",
        TenantStatus = null!
    };

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
}
