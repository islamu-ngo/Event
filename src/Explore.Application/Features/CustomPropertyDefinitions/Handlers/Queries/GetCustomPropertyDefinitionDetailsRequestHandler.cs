using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Exceptions;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Queries;

public class GetCustomPropertyDefinitionDetailsRequestHandler : IRequestHandler<GetCustomPropertyDefinitionDetailsRequest, CustomPropertyDefinitionDto>
{
    private readonly ICustomPropertyDefinitionRepository _customPropertyDefinitionRepository;
    private readonly IMapper _mapper;

    public GetCustomPropertyDefinitionDetailsRequestHandler(
        ICustomPropertyDefinitionRepository customPropertyDefinitionRepository,
        IMapper mapper)
    {
        _customPropertyDefinitionRepository = customPropertyDefinitionRepository;
        _mapper = mapper;
    }

    public async Task<CustomPropertyDefinitionDto> Handle(GetCustomPropertyDefinitionDetailsRequest request, CancellationToken cancellationToken)
    {
        var definition = await _customPropertyDefinitionRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            throw new NotFoundException(nameof(CustomPropertyDefinition), request.Id);
        }

        return _mapper.Map<CustomPropertyDefinitionDto>(definition);
    }
}
