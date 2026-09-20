using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;

public class DeleteEventSessionCustomPropertyDefinitionCommandHandler : ICommandHandler<DeleteEventSessionCustomPropertyDefinitionCommand, bool>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;
    private readonly IEventSessionCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly IUnitOfWork _unitOfWork;
    private readonly HybridCache _cache;

    public DeleteEventSessionCustomPropertyDefinitionCommandHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        IEventSessionCustomPropertyProjectionUpdater projectionUpdater,
        IUnitOfWork unitOfWork,
        HybridCache cache)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
        _projectionUpdater = projectionUpdater;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<bool> ExecuteAsync(DeleteEventSessionCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        var definition = await _sessionCustomPropertyRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            return false;
        }

        var deleted = await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await _projectionUpdater.RemoveForDefinitionAsync(request.Id, ct);
                return await _sessionCustomPropertyRepository.DeleteDefinition(request.Id, ct);
            },
            cancellationToken);

        if (!deleted)
        {
            return false;
        }

        await _cache.RemoveByTagAsync(
            SessionCustomPropertyCache.ListsBySession(definition.TenantId, definition.EventSessionId),
            CancellationToken.None);

        return true;
    }
}
