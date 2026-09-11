using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Queries;

public class GetEventSessionCustomPropertyValuesRequestHandler : IRequestHandler<GetEventSessionCustomPropertyValuesRequest, List<EventSessionCustomPropertyValueDto>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;

    public GetEventSessionCustomPropertyValuesRequestHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
    }

    public async Task<List<EventSessionCustomPropertyValueDto>> Handle(GetEventSessionCustomPropertyValuesRequest request, CancellationToken cancellationToken)
    {
        var values = await _sessionCustomPropertyRepository.GetValuesForSession(request.EventSessionId);
        return values.Select(CustomPropertyMapper.ToValue).ToList();
    }
}
