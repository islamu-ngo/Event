using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Queries;

public class GetEventSessionCustomPropertyDefinitionDetailsRequestHandler : IRequestHandler<GetEventSessionCustomPropertyDefinitionDetailsRequest, EventSessionCustomPropertyDefinitionDto>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;

    public GetEventSessionCustomPropertyDefinitionDetailsRequestHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
    }

    public async Task<EventSessionCustomPropertyDefinitionDto> Handle(GetEventSessionCustomPropertyDefinitionDetailsRequest request, CancellationToken cancellationToken)
    {
        var definition = await _sessionCustomPropertyRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            throw new NotFoundException(nameof(EventSessionCustomPropertyDefinition), request.Id);
        }

        return CustomPropertyMapper.ToDetail(definition);
    }
}
