using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Queries;

public class GetEventSessionCustomPropertyValuesRequestHandler : IQueryHandler<GetEventSessionCustomPropertyValuesRequest, List<EventSessionCustomPropertyValueDto>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;

    public GetEventSessionCustomPropertyValuesRequestHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
    }

    public async Task<List<EventSessionCustomPropertyValueDto>> QueryAsync(GetEventSessionCustomPropertyValuesRequest request, CancellationToken cancellationToken)
    {
        var values = await _sessionCustomPropertyRepository.GetValuesForSession(request.EventSessionId);
        return values.Select(CustomPropertyMapper.ToValue).ToList();
    }
}
