using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Queries;

public class GetEventSessionCustomPropertyValuesRequestHandler : IRequestHandler<GetEventSessionCustomPropertyValuesRequest, List<EventSessionCustomPropertyValueDto>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;
    private readonly IMapper _mapper;

    public GetEventSessionCustomPropertyValuesRequestHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        IMapper mapper)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
        _mapper = mapper;
    }

    public async Task<List<EventSessionCustomPropertyValueDto>> Handle(GetEventSessionCustomPropertyValuesRequest request, CancellationToken cancellationToken)
    {
        var values = await _sessionCustomPropertyRepository.GetValuesForSession(request.EventSessionId);
        return _mapper.Map<List<EventSessionCustomPropertyValueDto>>(values);
    }
}
