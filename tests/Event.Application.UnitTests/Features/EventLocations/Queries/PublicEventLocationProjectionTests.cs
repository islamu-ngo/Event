using AutoMapper;
using Event.Application.UnitTests.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Features.Agenda.Handlers.Queries;
using Explore.Application.Features.Agenda.Requests.Queries;
using Explore.Application.Features.EventAgendaItems.Handlers.Queries;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Features.EventPrograms.Handlers.Queries;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.EventSessionAgendaItems.Handlers.Queries;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using Explore.Application.Features.EventSessionGroups.Handlers.Queries;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Features.EventSessions.Handlers.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventLocations.Queries;

[Category("EventLocationPrivacy")]
public sealed class PublicEventLocationProjectionTests
{
    private const string PublicVenueName = "Purpose-limited venue";
    private const string PublicRoomName = "Purpose-limited room";

    [Test]
    public async Task SessionResponses_MaterializePublicLocationAndRedactLegacyFields()
    {
        var repository = Substitute.For<IEventSessionRepository>();
        var mapper = Substitute.For<IMapper>();
        var disclosureService = new RecordingDisclosureService();
        Guid tenantId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        Guid roomId = Guid.NewGuid();
        EventSession session = CreateSession(tenantId, eventId, roomId);
        Guid eventLocationId = session.EventLocationId!.Value;
        var detailDto = new EventSessionDto
        {
            Id = session.Id,
            EventId = eventId,
            EventTitle = "Public event",
            LocationId = Guid.NewGuid(),
            LocationFullName = "Private venue canary",
            LocationAddress = "Private address canary",
            LocationCity = "Private city canary",
            LocationCountry = "Private country canary",
            RoomId = Guid.NewGuid(),
            RoomName = "Private room canary"
        };
        var listDto = new EventSessionListDto
        {
            Id = session.Id,
            EventId = eventId,
            EventTitle = "Public event",
            LocationId = Guid.NewGuid(),
            LocationFullName = "Private venue canary",
            LocationCity = "Private city canary",
            RoomId = Guid.NewGuid(),
            RoomName = "Private room canary"
        };
        repository.GetPublicSessionWithDetailsAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        repository.GetPublicSessionsWithDetailsPagedAsync(1, 20, Arg.Any<CancellationToken>()).Returns(([session], 1));
        repository.GetPublicSessionsByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([session]);
        mapper.Map<EventSessionDto>(session).Returns(detailDto);
        mapper.Map<List<EventSessionListDto>>(Arg.Any<List<EventSession>>()).Returns([listDto]);

        EventSessionDto? detail = await new GetEventSessionDetailsRequestHandler(repository, mapper, disclosureService)
            .Handle(new GetEventSessionDetailsRequest { Id = session.Id }, CancellationToken.None);
        EventSessionListDto paged = (await new GetEventSessionListRequestHandler(
                repository,
                mapper,
                Substitute.For<ICustomPropertyQuotaResolver>(),
                Substitute.For<ITenantContext>(),
                disclosureService)
            .Handle(new GetEventSessionListRequest(), CancellationToken.None)).Items.Single();
        EventSessionListDto byEvent = (await new GetSessionsByEventRequestHandler(repository, mapper, disclosureService)
            .Handle(new GetSessionsByEventRequest { EventId = eventId }, CancellationToken.None)).Single();

        await AssertPublicLocationAsync(detail!.EventLocation, eventLocationId, expectRoom: true);
        await AssertPublicLocationAsync(paged.EventLocation, eventLocationId, expectRoom: true);
        await AssertPublicLocationAsync(byEvent.EventLocation, eventLocationId, expectRoom: true);
        await Assert.That(detail.LocationId is null
            && detail.LocationFullName is null
            && detail.LocationAddress is null
            && detail.LocationCity is null
            && detail.LocationCountry is null
            && detail.RoomId is null
            && detail.RoomName is null).IsTrue();
        await Assert.That(paged.LocationId is null
            && paged.LocationFullName is null
            && paged.LocationCity is null
            && paged.RoomId is null
            && paged.RoomName is null).IsTrue();
        await Assert.That(byEvent.LocationId is null
            && byEvent.LocationFullName is null
            && byEvent.LocationCity is null
            && byEvent.RoomId is null
            && byEvent.RoomName is null).IsTrue();
        await AssertPublicCallsAsync(disclosureService, expectedCallCount: 3, expectedRequestsPerCall: 1);
    }

    [Test]
    public async Task SessionGroupResponses_MaterializePublicLocationAndRedactLegacyFields()
    {
        var repository = Substitute.For<IEventSessionGroupRepository>();
        var mapper = Substitute.For<IMapper>();
        var disclosureService = new RecordingDisclosureService();
        Guid tenantId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        EventSessionGroup group = CreateSessionGroup(tenantId, eventId, Guid.NewGuid());
        Guid eventLocationId = group.EventLocationId!.Value;
        var detailDto = new EventSessionGroupDto
        {
            Id = group.Id,
            EventId = eventId,
            Name = group.Name,
            LocationId = Guid.NewGuid(),
            LocationName = "Private venue canary",
            RoomId = Guid.NewGuid(),
            RoomName = "Private room canary"
        };
        var listDto = new EventSessionGroupListDto
        {
            Id = group.Id,
            EventId = eventId,
            Name = group.Name,
            LocationId = Guid.NewGuid(),
            LocationName = "Private venue canary",
            RoomId = Guid.NewGuid(),
            RoomName = "Private room canary"
        };
        repository.GetPublicWithDetailsAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        repository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([group]);
        mapper.Map<EventSessionGroupDto>(group).Returns(detailDto);
        mapper.Map<List<EventSessionGroupListDto>>(Arg.Any<List<EventSessionGroup>>()).Returns([listDto]);

        EventSessionGroupDto? detail = await new GetEventSessionGroupDetailRequestHandler(repository, mapper, disclosureService)
            .Handle(new GetEventSessionGroupDetailRequest { Id = group.Id }, CancellationToken.None);
        EventSessionGroupListDto byEvent = (await new GetEventSessionGroupsByEventRequestHandler(repository, mapper, disclosureService)
            .Handle(new GetEventSessionGroupsByEventRequest { EventId = eventId }, CancellationToken.None)).Single();

        await AssertPublicLocationAsync(detail!.EventLocation, eventLocationId, expectRoom: true);
        await AssertPublicLocationAsync(byEvent.EventLocation, eventLocationId, expectRoom: true);
        await Assert.That(detail.LocationId is null
            && detail.LocationName is null
            && detail.RoomId is null
            && detail.RoomName is null).IsTrue();
        await Assert.That(byEvent.LocationId is null
            && byEvent.LocationName is null
            && byEvent.RoomId is null
            && byEvent.RoomName is null).IsTrue();
        await AssertPublicCallsAsync(disclosureService, expectedCallCount: 2, expectedRequestsPerCall: 1);
    }

    [Test]
    public async Task EventAgendaResponses_MaterializePublicLocationAndRedactLegacyFields()
    {
        var repository = Substitute.For<IEventAgendaItemRepository>();
        var mapper = Substitute.For<IMapper>();
        var disclosureService = new RecordingDisclosureService();
        Guid tenantId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        EventAgendaItem item = CreateEventAgendaItem(tenantId, eventId, Guid.NewGuid());
        Guid eventLocationId = item.EventLocationId!.Value;
        var detailDto = new EventAgendaItemDto
        {
            Id = item.Id,
            EventId = eventId,
            Title = item.Title,
            LocationId = Guid.NewGuid(),
            RoomId = Guid.NewGuid()
        };
        var listDto = new EventAgendaItemListDto
        {
            Id = item.Id,
            EventId = eventId,
            Title = item.Title
        };
        repository.GetPublicByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        repository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([item]);
        mapper.Map<EventAgendaItemDto>(item).Returns(detailDto);
        mapper.Map<List<EventAgendaItemListDto>>(Arg.Any<List<EventAgendaItem>>()).Returns([listDto]);

        EventAgendaItemDto? detail = await new GetEventAgendaItemDetailRequestHandler(repository, mapper, disclosureService)
            .Handle(new GetEventAgendaItemDetailRequest(item.Id), CancellationToken.None);
        EventAgendaItemListDto byEvent = (await new GetEventAgendaItemsByEventRequestHandler(repository, mapper, disclosureService)
            .Handle(new GetEventAgendaItemsByEventRequest(eventId), CancellationToken.None)).Single();

        await AssertPublicLocationAsync(detail!.EventLocation, eventLocationId, expectRoom: true);
        await AssertPublicLocationAsync(byEvent.EventLocation, eventLocationId, expectRoom: true);
        await Assert.That(detail.LocationId is null && detail.RoomId is null).IsTrue();
        await AssertPublicCallsAsync(disclosureService, expectedCallCount: 2, expectedRequestsPerCall: 1);
    }

    [Test]
    public async Task SessionAgendaResponses_MaterializePublicLocationAndRedactLegacyFields()
    {
        var repository = Substitute.For<IEventSessionAgendaItemRepository>();
        var mapper = Substitute.For<IMapper>();
        var disclosureService = new RecordingDisclosureService();
        Guid tenantId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        EventSessionAgendaItem item = CreateSessionAgendaItem(tenantId, eventId);
        Guid eventLocationId = item.EventLocationId!.Value;
        var detailDto = new EventSessionAgendaItemDto
        {
            Id = item.Id,
            EventId = eventId,
            EventSessionId = item.EventSessionId,
            Title = item.Title,
            LocationId = Guid.NewGuid(),
            LocationFullName = "Private venue canary"
        };
        var listDto = new EventSessionAgendaItemListDto
        {
            Id = item.Id,
            EventId = eventId,
            EventSessionId = item.EventSessionId,
            Title = item.Title,
            LocationFullName = "Private venue canary"
        };
        repository.GetPublicByIdWithDetailsAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        repository.GetPublicAgendaItemsWithDetailsPagedAsync(1, 20, Arg.Any<CancellationToken>()).Returns(([item], 1));
        repository.GetPublicBySessionAsync(item.EventSessionId, Arg.Any<CancellationToken>()).Returns([item]);
        mapper.Map<EventSessionAgendaItemDto>(item).Returns(detailDto);
        mapper.Map<List<EventSessionAgendaItemListDto>>(Arg.Any<List<EventSessionAgendaItem>>()).Returns([listDto]);

        EventSessionAgendaItemDto? detail = await new GetEventSessionAgendaItemDetailsRequestHandler(repository, mapper, disclosureService)
            .Handle(new GetEventSessionAgendaItemDetailsRequest { Id = item.Id }, CancellationToken.None);
        EventSessionAgendaItemListDto paged = (await new GetEventSessionAgendaItemListRequestHandler(repository, mapper, disclosureService)
            .Handle(new GetEventSessionAgendaItemListRequest(), CancellationToken.None)).Items.Single();
        EventSessionAgendaItemListDto bySession = (await new GetAgendaItemsBySessionRequestHandler(repository, mapper, disclosureService)
            .Handle(new GetAgendaItemsBySessionRequest { EventSessionId = item.EventSessionId }, CancellationToken.None)).Single();

        await AssertPublicLocationAsync(detail!.EventLocation, eventLocationId, expectRoom: false);
        await AssertPublicLocationAsync(paged.EventLocation, eventLocationId, expectRoom: false);
        await AssertPublicLocationAsync(bySession.EventLocation, eventLocationId, expectRoom: false);
        await Assert.That(detail.LocationId is null && detail.LocationFullName is null).IsTrue();
        await Assert.That(paged.LocationFullName).IsNull();
        await Assert.That(bySession.LocationFullName).IsNull();
        await AssertPublicCallsAsync(disclosureService, expectedCallCount: 3, expectedRequestsPerCall: 1);
    }

    [Test]
    public async Task MergedAgenda_CombinesPlacementsAndSuppressesConflictingRoomContext()
    {
        var eventRepository = Substitute.For<IEventRepository>();
        var eventDayRepository = Substitute.For<IEventDayRepository>();
        var sessionRepository = Substitute.For<IEventSessionRepository>();
        var agendaRepository = Substitute.For<IEventAgendaItemRepository>();
        var disclosureService = new RecordingDisclosureService();
        Guid tenantId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        Explore.Domain.Event parentEvent = CreatePublicEvent(tenantId, eventId);
        EventLocation sharedLocation = EventLocation.CreatePhysical(
            tenantId,
            eventId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow);
        EventSession session = CreateSession(tenantId, eventId, Guid.NewGuid(), sharedLocation);
        EventAgendaItem agendaItem = CreateEventAgendaItem(tenantId, eventId, Guid.NewGuid(), sharedLocation);
        eventRepository.GetById(eventId).Returns(parentEvent);
        eventRepository.IsPubliclyEligibleAsync(tenantId, eventId, Arg.Any<CancellationToken>()).Returns(true);
        eventDayRepository.GetByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([]);
        sessionRepository.GetPublicSessionsByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([session]);
        agendaRepository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([agendaItem]);
        var handler = new GetEventAgendaProjectionRequestHandler(
            eventRepository,
            eventDayRepository,
            sessionRepository,
            agendaRepository,
            disclosureService);

        var result = await handler.Handle(
            new GetEventAgendaProjectionRequest { EventId = eventId },
            CancellationToken.None);
        var entries = result!.Days.Single().Entries;

        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries.All(entry => entry.EventLocation?.EventLocationId == sharedLocation.Id)).IsTrue();
        await Assert.That(entries.All(entry => entry.EventLocation?.Fields?.RoomName is null)).IsTrue();
        await Assert.That(entries.All(entry => entry.LocationId is null && entry.RoomId is null)).IsTrue();
        await AssertPublicCallsAsync(disclosureService, expectedCallCount: 1, expectedRequestsPerCall: 2);
        await Assert.That(disclosureService.Calls.Single().Select(request => request.RoomId).Distinct().Count())
            .IsEqualTo(2);
    }

    [Test]
    public async Task ProgramSummary_CombinesSessionAndGroupPlacementsInOnePublicBatch()
    {
        var eventRepository = Substitute.For<IEventRepository>();
        var sessionRepository = Substitute.For<IEventSessionRepository>();
        var groupRepository = Substitute.For<IEventSessionGroupRepository>();
        var agendaRepository = Substitute.For<IEventAgendaItemRepository>();
        var disclosureService = new RecordingDisclosureService();
        Guid tenantId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        Explore.Domain.Event parentEvent = CreatePublicEvent(tenantId, eventId);
        EventSessionGroup group = CreateSessionGroup(tenantId, eventId, Guid.NewGuid());
        EventSession session = CreateSession(tenantId, eventId, Guid.NewGuid());
        session.SessionGroups.Add(new EventSessionGroupSession
        {
            Id = Guid.NewGuid(),
            EventSessionGroupId = group.Id,
            EventSessionGroup = group,
            EventSessionId = session.Id,
            EventSession = session,
            EventId = eventId,
            Event = parentEvent,
            IsPrimary = true,
            SortOrder = 1,
            TenantId = tenantId,
            Tenant = null!
        });
        eventRepository.GetEventWithDetails(eventId).Returns(parentEvent);
        eventRepository.IsPubliclyEligibleAsync(tenantId, eventId, Arg.Any<CancellationToken>()).Returns(true);
        sessionRepository.GetPublicSessionsByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([session]);
        groupRepository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([group]);
        agendaRepository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([]);
        var handler = new GetEventProgramSummaryRequestHandler(
            eventRepository,
            sessionRepository,
            groupRepository,
            agendaRepository,
            disclosureService);

        var summary = await handler.Handle(
            new GetEventProgramSummaryRequest(eventId),
            CancellationToken.None);
        var groupDto = summary!.Sections.Single().SessionGroups.Single();
        var itemDto = groupDto.Days.Single().Items.Single();

        await AssertPublicLocationAsync(groupDto.EventLocation, group.EventLocationId!.Value, expectRoom: true);
        await AssertPublicLocationAsync(itemDto.EventLocation, session.EventLocationId!.Value, expectRoom: true);
        await Assert.That(groupDto.LocationName is null && groupDto.RoomName is null).IsTrue();
        await Assert.That(itemDto.LocationName is null && itemDto.RoomName is null).IsTrue();
        await AssertPublicCallsAsync(disclosureService, expectedCallCount: 1, expectedRequestsPerCall: 2);
        await Assert.That(disclosureService.Calls.Single().Select(request => request.EventLocationId).Distinct().Count())
            .IsEqualTo(2);
    }

    [Test]
    public async Task Cancellation_StopsBeforePublicLocationMaterialization()
    {
        var repository = Substitute.For<IEventSessionRepository>();
        var mapper = Substitute.For<IMapper>();
        var disclosureService = new RecordingDisclosureService();
        EventSession session = CreateSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var mapped = new EventSessionDto
        {
            Id = session.Id,
            EventId = session.EventId,
            EventTitle = "Public event"
        };
        repository.GetPublicSessionWithDetailsAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        mapper.Map<EventSessionDto>(session).Returns(mapped);
        var handler = new GetEventSessionDetailsRequestHandler(repository, mapper, disclosureService);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(async () => await handler.Handle(
                new GetEventSessionDetailsRequest { Id = session.Id },
                cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(mapped.EventLocation).IsNull();
        await Assert.That(disclosureService.Calls).IsEmpty();
    }

    private static EventSession CreateSession(
        Guid tenantId,
        Guid eventId,
        Guid roomId,
        EventLocation? eventLocation = null)
    {
        EventSession session = DataBuilder.EventSession.Generate();
        session.EventId = eventId;
        session.TenantId = tenantId;
        session.Event = null!;
        session.Tenant = null!;
        session.StartTime = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
        session.EndTime = session.StartTime.Value.AddHours(1);
        session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        eventLocation ??= EventLocation.CreatePhysical(
            tenantId,
            eventId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow);
        session.AssignEventLocation(eventLocation);
        session.RoomId = roomId;
        return session;
    }

    private static EventSessionGroup CreateSessionGroup(Guid tenantId, Guid eventId, Guid roomId)
    {
        var group = new EventSessionGroup
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            Name = "Public section",
            TenantId = tenantId,
            Tenant = null!,
            IsPublished = true
        };
        group.AssignEventLocation(EventLocation.CreatePhysical(
            tenantId,
            eventId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow));
        group.RoomId = roomId;
        return group;
    }

    private static EventAgendaItem CreateEventAgendaItem(
        Guid tenantId,
        Guid eventId,
        Guid roomId,
        EventLocation? eventLocation = null)
    {
        EventAgendaItem item = DataBuilder.EventAgendaItem.Generate();
        item.EventId = eventId;
        item.TenantId = tenantId;
        item.Event = null!;
        item.Tenant = null!;
        item.StartTime = new DateTimeOffset(2026, 7, 20, 10, 30, 0, TimeSpan.Zero);
        item.EndTime = item.StartTime.AddMinutes(30);
        item.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        eventLocation ??= EventLocation.CreatePhysical(
            tenantId,
            eventId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow);
        item.AssignEventLocation(eventLocation);
        item.RoomId = roomId;
        return item;
    }

    private static EventSessionAgendaItem CreateSessionAgendaItem(Guid tenantId, Guid eventId)
    {
        EventSession session = CreateSession(tenantId, eventId, Guid.NewGuid());
        EventSessionAgendaItem item = DataBuilder.EventSessionAgendaItem.Generate();
        item.EventSessionId = session.Id;
        item.EventSession = session;
        item.TenantId = tenantId;
        item.Tenant = null!;
        item.AssignEventLocation(session.EventLocation!);
        return item;
    }

    private static Explore.Domain.Event CreatePublicEvent(Guid tenantId, Guid eventId)
    {
        Explore.Domain.Event parentEvent = DataBuilder.EventWithStatus(EventStatusEnum.Published).Generate();
        parentEvent.Id = eventId;
        parentEvent.TenantId = tenantId;
        parentEvent.Title = "Public event";
        parentEvent.VisibilityTypeId = (int)VisibilityTypeEnum.Public;
        parentEvent.Timezone = "UTC";
        parentEvent.EventTimeZoneId = "UTC";
        return parentEvent;
    }

    private static async Task AssertPublicLocationAsync(
        Explore.Application.DTOs.Location.EventLocationPublicDto? eventLocation,
        Guid expectedEventLocationId,
        bool expectRoom)
    {
        await Assert.That(eventLocation).IsNotNull();
        await Assert.That(eventLocation!.EventLocationId).IsEqualTo(expectedEventLocationId);
        await Assert.That(eventLocation.State).IsEqualTo(EventLocationDisclosureState.Available);
        await Assert.That(eventLocation.Fields?.VenueName).IsEqualTo(PublicVenueName);
        await Assert.That(eventLocation.Fields?.RoomName).IsEqualTo(expectRoom ? PublicRoomName : null);
    }

    private static async Task AssertPublicCallsAsync(
        RecordingDisclosureService disclosureService,
        int expectedCallCount,
        int expectedRequestsPerCall)
    {
        await Assert.That(disclosureService.Calls.Count).IsEqualTo(expectedCallCount);
        await Assert.That(disclosureService.Calls.All(call => call.Count == expectedRequestsPerCall)).IsTrue();
        await Assert.That(disclosureService.Calls
            .SelectMany(call => call)
            .All(request => request.Purpose == EventLocationDisclosurePurpose.Public
                && request.RequesterUserId is null
                && request.TenantId != Guid.Empty
                && request.EventId != Guid.Empty
                && request.EventLocationId != Guid.Empty)).IsTrue();
    }

    private sealed class RecordingDisclosureService : IEventLocationDisclosureService
    {
        public List<IReadOnlyList<EventLocationDisclosureRequest>> Calls { get; } = [];

        public Task<IReadOnlyDictionary<Guid, EventLocationDisclosureResult>> ResolveManyAsync(
            IReadOnlyCollection<EventLocationDisclosureRequest> requests,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventLocationDisclosureRequest[] requestArray = requests.ToArray();
            Calls.Add(requestArray);
            IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results = requestArray
                .GroupBy(request => request.EventLocationId)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        Guid?[] roomIds = group.Select(request => request.RoomId).Distinct().ToArray();
                        string? roomName = roomIds.Length == 1 && roomIds[0].HasValue
                            ? PublicRoomName
                            : null;
                        return EventLocationDisclosureResult.Public(
                            group.Key,
                            EventLocationDisclosureState.Available,
                            new EventLocationDisclosureValues(
                                City: "Brussels",
                                VenueName: PublicVenueName,
                                RoomName: roomName));
                    });
            return Task.FromResult(results);
        }
    }
}
