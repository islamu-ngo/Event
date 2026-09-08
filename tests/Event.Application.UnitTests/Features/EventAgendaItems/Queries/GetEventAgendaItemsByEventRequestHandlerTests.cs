using AutoMapper;
using Event.Application.UnitTests.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Handlers.Queries;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Domain;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.EventAgendaItems.Queries;

public class GetEventAgendaItemsByEventRequestHandlerTests
{
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IMapper _mapper;
    private readonly IEventLocationDisclosureService _disclosureService;
    private readonly GetEventAgendaItemsByEventRequestHandler _handler;

    public GetEventAgendaItemsByEventRequestHandlerTests()
    {
        _eventAgendaItemRepository = Substitute.For<IEventAgendaItemRepository>();
        _mapper = Substitute.For<IMapper>();
        _disclosureService = Substitute.For<IEventLocationDisclosureService>();

        _handler = new GetEventAgendaItemsByEventRequestHandler(
            _eventAgendaItemRepository,
            _mapper,
            _disclosureService);
    }

    [Test]
    public async Task Handle_WithExistingAgendaItems_ReturnsMappedList()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var request = new GetEventAgendaItemsByEventRequest(eventId);

        var agendaItems = new List<EventAgendaItem>
        {
            DataBuilder.EventAgendaItem.Generate(),
            DataBuilder.EventAgendaItem.Generate(),
            DataBuilder.EventAgendaItem.Generate()
        };

        var expectedDtos = new List<EventAgendaItemListDto>
        {
            new() { Id = agendaItems[0].Id, Title = "Item 1" },
            new() { Id = agendaItems[1].Id, Title = "Item 2" },
            new() { Id = agendaItems[2].Id, Title = "Item 3" }
        };

        _eventAgendaItemRepository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns(agendaItems);
        _mapper.Map<List<EventAgendaItemListDto>>(agendaItems).Returns(expectedDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Handle_WithNoAgendaItems_ReturnsEmptyList()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var request = new GetEventAgendaItemsByEventRequest(eventId);

        _eventAgendaItemRepository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new List<EventAgendaItem>());
        _mapper.Map<List<EventAgendaItemListDto>>(Arg.Any<List<EventAgendaItem>>())
            .Returns(new List<EventAgendaItemListDto>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(0);
    }
}
