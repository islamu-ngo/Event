using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventType;
using Explore.Application.Features.EventTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTypes.Handlers.Queries;

public class GetEventTypeListRequestHandler : IQueryHandler<GetEventTypeListRequest, List<EventTypeListDto>>
{
    private readonly IEventTypeRepository _eventTypeRepository;

    public GetEventTypeListRequestHandler(IEventTypeRepository eventTypeRepository)
    {
        _eventTypeRepository = eventTypeRepository;
    }

    public async Task<List<EventTypeListDto>> QueryAsync(GetEventTypeListRequest request, CancellationToken cancellationToken)
    {
        var eventTypes = await _eventTypeRepository.GetAll();
        return eventTypes.Select(EventMapper.ToListItem).ToList();
    }
}
