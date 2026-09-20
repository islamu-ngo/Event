using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Exceptions;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Domain;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Queries;

public class GetCustomPropertyDefinitionDetailsQueryHandler : IQueryHandler<GetCustomPropertyDefinitionDetailsQuery, CustomPropertyDefinitionDto>
{
    private readonly ICustomPropertyDefinitionRepository _customPropertyDefinitionRepository;

    public GetCustomPropertyDefinitionDetailsQueryHandler(
        ICustomPropertyDefinitionRepository customPropertyDefinitionRepository)
    {
        _customPropertyDefinitionRepository = customPropertyDefinitionRepository;
    }

    public async Task<CustomPropertyDefinitionDto> QueryAsync(GetCustomPropertyDefinitionDetailsQuery request, CancellationToken cancellationToken)
    {
        var definition = await _customPropertyDefinitionRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            throw new NotFoundException(nameof(CustomPropertyDefinition), request.Id);
        }

        return CustomPropertyMapper.ToDetail(definition);
    }
}
