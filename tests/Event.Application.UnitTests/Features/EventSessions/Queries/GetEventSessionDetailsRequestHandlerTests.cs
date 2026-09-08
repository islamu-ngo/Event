using AutoMapper;
using Event.Application.UnitTests.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Handlers.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Domain;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.EventSessions.Queries;

[Category("EventLocationPrivacy")]
public class GetEventSessionDetailsRequestHandlerTests
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IMapper _mapper;
    private readonly IEventLocationDisclosureService _disclosureService;
    private readonly GetEventSessionDetailsRequestHandler _handler;

    public GetEventSessionDetailsRequestHandlerTests()
    {
        _eventSessionRepository = Substitute.For<IEventSessionRepository>();
        _mapper = Substitute.For<IMapper>();
        _disclosureService = Substitute.For<IEventLocationDisclosureService>();

        _handler = new GetEventSessionDetailsRequestHandler(
            _eventSessionRepository,
            _mapper,
            _disclosureService);
    }

    [Test]
    public async Task Handle_WithExistingSession_ReturnsSessionDto()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var request = new GetEventSessionDetailsRequest { Id = sessionId };

        var eventSession = DataBuilder.EventSession.Generate();
        eventSession.Id = sessionId;
        eventSession.Title = "Test Session";

        var expectedDto = new EventSessionDto
        {
            Id = sessionId,
            Title = "Test Session",
            EventTitle = string.Empty
        };

        _eventSessionRepository
            .GetPublicSessionWithDetailsAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(eventSession);
        _mapper.Map<EventSessionDto>(eventSession).Returns(expectedDto);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(sessionId);
        await Assert.That(result.Title).IsEqualTo("Test Session");
    }

    [Test]
    public async Task Handle_WithNonExistentSession_ReturnsNull()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var request = new GetEventSessionDetailsRequest { Id = sessionId };

        _eventSessionRepository
            .GetPublicSessionWithDetailsAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns((EventSession?)null);
        _mapper.Map<EventSessionDto>(Arg.Any<EventSession?>()).Returns((EventSessionDto?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_DoesNotExposePhysicalLocationDetails()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var request = new GetEventSessionDetailsRequest { Id = sessionId };

        var eventSession = DataBuilder.EventSession.Generate();
        eventSession.Id = sessionId;
        eventSession.LocationId = locationId;
        eventSession.Location = DataBuilder.Location.Generate();
        eventSession.Location.Id = locationId;
        eventSession.Location.FullName = "Test Location";

        var expectedDto = new EventSessionDto
        {
            Id = sessionId,
            LocationId = locationId,
            LocationFullName = "Test Location",
            EventTitle = string.Empty
        };

        _eventSessionRepository
            .GetPublicSessionWithDetailsAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(eventSession);
        _mapper.Map<EventSessionDto>(eventSession).Returns(expectedDto);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.LocationId).IsNull();
        await Assert.That(result.LocationFullName).IsNull();
        await Assert.That(result.LocationAddress).IsNull();
        await Assert.That(result.LocationCity).IsNull();
        await Assert.That(result.LocationCountry).IsNull();
        await Assert.That(result.RoomId).IsNull();
        await Assert.That(result.RoomName).IsNull();
    }
}

[Category("EventLocationPrivacy")]
public sealed class PublicEventSessionListLocationPrivacyTests
{
    private readonly IEventSessionRepository _repository = Substitute.For<IEventSessionRepository>();
    private readonly IMapper _mapper = Substitute.For<IMapper>();
    private readonly IEventLocationDisclosureService _disclosureService = Substitute.For<IEventLocationDisclosureService>();

    [Test]
    public async Task ByEvent_DoesNotExposePhysicalLocationDetails()
    {
        var eventId = Guid.NewGuid();
        var sessions = new List<EventSession> { DataBuilder.EventSession.Generate() };
        var mapped = new List<EventSessionListDto> { CreatePhysicalSessionDto(eventId) };
        mapped[0] = mapped[0] with { Id = sessions[0].Id };
        _repository.GetPublicSessionsByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns(sessions);
        _mapper.Map<List<EventSessionListDto>>(sessions).Returns(mapped);
        var handler = new GetSessionsByEventRequestHandler(_repository, _mapper, _disclosureService);

        var result = await handler.Handle(
            new GetSessionsByEventRequest { EventId = eventId },
            CancellationToken.None);

        await AssertPhysicalLocationIsRedactedAsync(result.Single());
    }

    [Test]
    public async Task PaginatedList_DoesNotExposePhysicalLocationDetails()
    {
        var sessions = new List<EventSession> { DataBuilder.EventSession.Generate() };
        var mapped = new List<EventSessionListDto> { CreatePhysicalSessionDto(Guid.NewGuid()) };
        mapped[0] = mapped[0] with { Id = sessions[0].Id };
        _repository.GetPublicSessionsWithDetailsPagedAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns((sessions, 1));
        _mapper.Map<List<EventSessionListDto>>(sessions).Returns(mapped);
        var handler = new GetEventSessionListRequestHandler(
            _repository,
            _mapper,
            Substitute.For<ICustomPropertyQuotaResolver>(),
            Substitute.For<ITenantContext>(),
            _disclosureService);

        var result = await handler.Handle(new GetEventSessionListRequest(), CancellationToken.None);

        await AssertPhysicalLocationIsRedactedAsync(result.Items.Single());
    }

    private static EventSessionListDto CreatePhysicalSessionDto(Guid eventId) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        EventTitle = "Public event",
        LocationId = Guid.NewGuid(),
        LocationFullName = "Private venue",
        LocationCity = "Private city",
        RoomId = Guid.NewGuid(),
        RoomName = "Private room"
    };

    private static async Task AssertPhysicalLocationIsRedactedAsync(EventSessionListDto dto)
    {
        await Assert.That(dto.LocationId).IsNull();
        await Assert.That(dto.LocationFullName).IsNull();
        await Assert.That(dto.LocationCity).IsNull();
        await Assert.That(dto.RoomId).IsNull();
        await Assert.That(dto.RoomName).IsNull();
    }
}
