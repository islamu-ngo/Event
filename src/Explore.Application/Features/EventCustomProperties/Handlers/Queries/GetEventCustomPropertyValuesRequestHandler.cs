using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Queries;

public class GetEventCustomPropertyValuesRequestHandler : IQueryHandler<GetEventCustomPropertyValuesRequest, List<EventCustomPropertyValueDto>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IPrivateEventManagementReader? _privateManagement;

    public GetEventCustomPropertyValuesRequestHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IPrivateEventManagementReader? privateManagement = null)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _privateManagement = privateManagement;
    }

    public async Task<List<EventCustomPropertyValueDto>> QueryAsync(GetEventCustomPropertyValuesRequest request, CancellationToken cancellationToken)
    {
        var values = _privateManagement is null ? await _eventCustomPropertyRepository.GetValuesForEvent(request.EventId)
            : (await _privateManagement.ReadAsync(new(PrivateEventManagementReadKind.CustomProperties, request.EventId), cancellationToken))
                .Values.OrderBy(value => value.Ordinal).ToList();
        return values.Select(CustomPropertyMapper.ToValue).ToList();
    }
}
