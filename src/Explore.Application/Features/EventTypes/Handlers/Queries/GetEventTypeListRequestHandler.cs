using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventType;
using Explore.Application.Features.EventTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventTypes.Handlers.Queries;

public class GetEventTypeListRequestHandler : IRequestHandler<GetEventTypeListRequest, List<EventTypeListDto>>
{
    private readonly IEventTypeRepository _eventTypeRepository;
    private readonly IMapper _mapper;

    public GetEventTypeListRequestHandler(IEventTypeRepository eventTypeRepository, IMapper mapper)
    {
        _eventTypeRepository = eventTypeRepository;
        _mapper = mapper;
    }

    public async Task<List<EventTypeListDto>> Handle(GetEventTypeListRequest request, CancellationToken cancellationToken)
    {
        var eventTypes = await _eventTypeRepository.GetAll();
        return _mapper.Map<List<EventTypeListDto>>(eventTypes);
    }
}
