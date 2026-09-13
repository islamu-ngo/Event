using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;

public class DeleteCustomPropertyDefinitionCommandHandler : IRequestHandler<DeleteCustomPropertyDefinitionCommand, bool>
{
    private readonly ICustomPropertyDefinitionRepository _customPropertyDefinitionRepository;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteCustomPropertyDefinitionCommandHandler(
        ICustomPropertyDefinitionRepository customPropertyDefinitionRepository,
        HybridCache cache,
        IUnitOfWork unitOfWork)
    {
        _customPropertyDefinitionRepository = customPropertyDefinitionRepository;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(DeleteCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        var definition = await _customPropertyDefinitionRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            return false;
        }

        var deleted = await _unitOfWork.ExecuteInTransactionAsync(
            ct => _customPropertyDefinitionRepository.DeleteDefinition(request.Id, ct),
            cancellationToken);
        if (!deleted)
        {
            return false;
        }

        await _cache.RemoveByTagAsync(
            CacheTags.CustomPropertyDefinitionListsByScope(definition.TenantId, definition.EntityTypeName),
            CancellationToken.None);

        return true;
    }
}
