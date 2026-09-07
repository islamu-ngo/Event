using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;

public class DeleteCustomPropertyDefinitionCommandHandler : IRequestHandler<DeleteCustomPropertyDefinitionCommand, bool>
{
    private readonly ICustomPropertyDefinitionRepository _customPropertyDefinitionRepository;
    private readonly HybridCache _cache;

    public DeleteCustomPropertyDefinitionCommandHandler(
        ICustomPropertyDefinitionRepository customPropertyDefinitionRepository,
        HybridCache cache)
    {
        _customPropertyDefinitionRepository = customPropertyDefinitionRepository;
        _cache = cache;
    }

    public async Task<bool> Handle(DeleteCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        var definition = await _customPropertyDefinitionRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            return false;
        }

        var deleted = await _customPropertyDefinitionRepository.DeleteDefinition(request.Id, cancellationToken);
        if (!deleted)
        {
            return false;
        }

        await _cache.RemoveAsync($"custom-property-definitions:list:{definition.EntityTypeName}:1:{PaginatedResult<object>.DefaultPageSize}", cancellationToken);
        await _cache.RemoveAsync($"custom-property-definitions:detail:{definition.Id}", cancellationToken);

        return true;
    }
}
