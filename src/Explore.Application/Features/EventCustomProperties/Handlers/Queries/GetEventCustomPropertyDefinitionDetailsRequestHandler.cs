using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Queries;

public class GetEventCustomPropertyDefinitionDetailsRequestHandler : IRequestHandler<GetEventCustomPropertyDefinitionDetailsRequest, EventCustomPropertyDefinitionDto>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IMapper _mapper;

    public GetEventCustomPropertyDefinitionDetailsRequestHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IMapper mapper)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _mapper = mapper;
    }

    public async Task<EventCustomPropertyDefinitionDto> Handle(GetEventCustomPropertyDefinitionDetailsRequest request, CancellationToken cancellationToken)
    {
        var definition = await _eventCustomPropertyRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            throw new NotFoundException(nameof(EventCustomPropertyDefinition), request.Id);
        }

        return _mapper.Map<EventCustomPropertyDefinitionDto>(definition);
    }
}
