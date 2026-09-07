using AutoMapper;
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
    private readonly IMapper _mapper;

    public GetEventSessionCustomPropertyDefinitionDetailsRequestHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        IMapper mapper)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
        _mapper = mapper;
    }

    public async Task<EventSessionCustomPropertyDefinitionDto> Handle(GetEventSessionCustomPropertyDefinitionDetailsRequest request, CancellationToken cancellationToken)
    {
        var definition = await _sessionCustomPropertyRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            throw new NotFoundException(nameof(EventSessionCustomPropertyDefinition), request.Id);
        }

        return _mapper.Map<EventSessionCustomPropertyDefinitionDto>(definition);
    }
}
