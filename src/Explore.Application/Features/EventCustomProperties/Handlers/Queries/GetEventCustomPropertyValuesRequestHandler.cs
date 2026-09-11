using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Queries;

public class GetEventCustomPropertyValuesRequestHandler : IRequestHandler<GetEventCustomPropertyValuesRequest, List<EventCustomPropertyValueDto>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;

    public GetEventCustomPropertyValuesRequestHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
    }

    public async Task<List<EventCustomPropertyValueDto>> Handle(GetEventCustomPropertyValuesRequest request, CancellationToken cancellationToken)
    {
        var values = await _eventCustomPropertyRepository.GetValuesForEvent(request.EventId);
        return values.Select(CustomPropertyMapper.ToValue).ToList();
    }
}
