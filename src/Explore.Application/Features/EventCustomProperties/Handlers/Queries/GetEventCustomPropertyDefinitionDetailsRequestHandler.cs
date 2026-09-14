using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Domain;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Queries;

public class GetEventCustomPropertyDefinitionDetailsRequestHandler : IQueryHandler<GetEventCustomPropertyDefinitionDetailsRequest, EventCustomPropertyDefinitionDto>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;

    public GetEventCustomPropertyDefinitionDetailsRequestHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
    }

    public async Task<EventCustomPropertyDefinitionDto> QueryAsync(GetEventCustomPropertyDefinitionDetailsRequest request, CancellationToken cancellationToken)
    {
        var definition = await _eventCustomPropertyRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            throw new NotFoundException(nameof(EventCustomPropertyDefinition), request.Id);
        }

        return CustomPropertyMapper.ToDetail(definition);
    }
}
