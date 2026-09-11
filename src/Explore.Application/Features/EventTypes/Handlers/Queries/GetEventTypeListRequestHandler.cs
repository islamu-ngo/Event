using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventType;
using Explore.Application.Features.EventTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventTypes.Handlers.Queries;

public class GetEventTypeListRequestHandler : IRequestHandler<GetEventTypeListRequest, List<EventTypeListDto>>
{
    private readonly IEventTypeRepository _eventTypeRepository;

    public GetEventTypeListRequestHandler(IEventTypeRepository eventTypeRepository)
    {
        _eventTypeRepository = eventTypeRepository;
    }

    public async Task<List<EventTypeListDto>> Handle(GetEventTypeListRequest request, CancellationToken cancellationToken)
    {
        var eventTypes = await _eventTypeRepository.GetAll();
        return eventTypes.Select(EventMapper.ToListItem).ToList();
    }
}
