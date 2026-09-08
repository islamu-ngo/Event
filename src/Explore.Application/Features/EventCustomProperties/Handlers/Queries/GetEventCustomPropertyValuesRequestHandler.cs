using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Queries;

public class GetEventCustomPropertyValuesRequestHandler : IRequestHandler<GetEventCustomPropertyValuesRequest, List<EventCustomPropertyValueDto>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IMapper _mapper;

    public GetEventCustomPropertyValuesRequestHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IMapper mapper)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _mapper = mapper;
    }

    public async Task<List<EventCustomPropertyValueDto>> Handle(GetEventCustomPropertyValuesRequest request, CancellationToken cancellationToken)
    {
        var values = await _eventCustomPropertyRepository.GetValuesForEvent(request.EventId);
        return _mapper.Map<List<EventCustomPropertyValueDto>>(values);
    }
}
