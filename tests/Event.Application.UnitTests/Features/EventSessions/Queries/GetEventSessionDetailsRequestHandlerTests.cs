using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Handlers.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventSessions.Queries;

[Category("EventLocationPrivacy")]
[Category("EventSessionMapping")]
public class GetEventSessionDetailsRequestHandlerTests
{
    private readonly IEventSessionRepository _repository = Substitute.For<IEventSessionRepository>();
    private readonly IEventLocationDisclosureService _disclosureService = Substitute.For<IEventLocationDisclosureService>();

    [Test]
    public async Task Handle_WithExistingSession_ReturnsSessionDto()
    {
        var session = CreateSession();
        _repository.GetPublicSessionWithDetailsAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var result = await new GetEventSessionDetailsRequestHandler(_repository, _disclosureService)
            .Handle(new GetEventSessionDetailsRequest { Id = session.Id }, CancellationToken.None);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(session.Id);
        await Assert.That(result.Title).IsEqualTo("Test Session");
    }

    [Test]
    public async Task Handle_WithNonExistentSession_ReturnsNull()
    {
        var id = Guid.Parse("01900000-0000-7000-8000-000000000001");
        _repository.GetPublicSessionWithDetailsAsync(id, Arg.Any<CancellationToken>()).Returns((EventSession?)null);
        var result = await new GetEventSessionDetailsRequestHandler(_repository, _disclosureService)
            .Handle(new GetEventSessionDetailsRequest { Id = id }, CancellationToken.None);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_DoesNotExposePhysicalLocationDetails()
    {
        var session = CreateSession();
        _repository.GetPublicSessionWithDetailsAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var result = await new GetEventSessionDetailsRequestHandler(_repository, _disclosureService)
            .Handle(new GetEventSessionDetailsRequest { Id = session.Id }, CancellationToken.None);
        await Assert.That(result!.LocationId).IsNull();
        await Assert.That(result.LocationFullName).IsNull();
        await Assert.That(result.LocationAddress).IsNull();
        await Assert.That(result.LocationCity).IsNull();
        await Assert.That(result.LocationCountry).IsNull();
        await Assert.That(result.RoomId).IsNull();
        await Assert.That(result.RoomName).IsNull();
    }

    internal static EventSession CreateSession()
    {
        var location = new Location { FullName = "Private venue", City = "Private city", Country = "Belgium", Tenant = null! };
        location.SetManualAddress("Private address", "1000");
        return new EventSession
        {
            Id = Guid.Parse("01900000-0000-7000-8000-000000000001"),
            EventId = Guid.Parse("01900000-0000-7000-8000-000000000002"),
            Event = null!, Tenant = null!, Title = "Test Session",
            LocationId = Guid.Parse("01900000-0000-7000-8000-000000000003"), Location = location,
            RoomId = Guid.Parse("01900000-0000-7000-8000-000000000004"),
            Room = new LocationRoom { Name = "Private room", Location = location, Tenant = null! }
        };
    }
}

[Category("EventLocationPrivacy")]
[Category("EventSessionMapping")]
public sealed class PublicEventSessionListLocationPrivacyTests
{
    private readonly IEventSessionRepository _repository = Substitute.For<IEventSessionRepository>();
    private readonly IEventLocationDisclosureService _disclosureService = Substitute.For<IEventLocationDisclosureService>();

    [Test]
    public async Task ByEvent_DoesNotExposePhysicalLocationDetails()
    {
        var session = GetEventSessionDetailsRequestHandlerTests.CreateSession();
        _repository.GetPublicSessionsByEventAsync(session.EventId, Arg.Any<CancellationToken>()).Returns([session]);
        var result = await new GetSessionsByEventRequestHandler(_repository, _disclosureService)
            .Handle(new GetSessionsByEventRequest { EventId = session.EventId }, CancellationToken.None);
        await AssertPhysicalLocationIsRedactedAsync(result.Single());
    }

    [Test]
    public async Task PaginatedList_DoesNotExposePhysicalLocationDetails()
    {
        var session = GetEventSessionDetailsRequestHandlerTests.CreateSession();
        _repository.GetPublicSessionsWithDetailsPagedAsync(1, 20, Arg.Any<CancellationToken>()).Returns(([session], 1));
        var handler = new GetEventSessionListRequestHandler(
            _repository, Substitute.For<ICustomPropertyQuotaResolver>(), Substitute.For<ITenantContext>(), _disclosureService);
        var result = await handler.Handle(new GetEventSessionListRequest(), CancellationToken.None);
        await AssertPhysicalLocationIsRedactedAsync(result.Items.Single());
        await Assert.That(result.TotalCount).IsEqualTo(1);
    }

    private static async Task AssertPhysicalLocationIsRedactedAsync(EventSessionListDto dto)
    {
        await Assert.That(dto.LocationId).IsNull();
        await Assert.That(dto.LocationFullName).IsNull();
        await Assert.That(dto.LocationCity).IsNull();
        await Assert.That(dto.RoomId).IsNull();
        await Assert.That(dto.RoomName).IsNull();
    }
}
