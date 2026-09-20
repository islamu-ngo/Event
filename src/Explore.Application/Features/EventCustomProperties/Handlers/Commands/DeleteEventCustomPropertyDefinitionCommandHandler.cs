using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Commands;

public class DeleteEventCustomPropertyDefinitionCommandHandler : ICommandHandler<DeleteEventCustomPropertyDefinitionCommand, bool>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IEventCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly IUnitOfWork _unitOfWork;
    private readonly HybridCache _cache;

    public DeleteEventCustomPropertyDefinitionCommandHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IEventCustomPropertyProjectionUpdater projectionUpdater,
        IUnitOfWork unitOfWork,
        HybridCache cache)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _projectionUpdater = projectionUpdater;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<bool> ExecuteAsync(DeleteEventCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        var definition = await _eventCustomPropertyRepository.GetDefinitionWithDetails(request.Id);
        if (definition == null)
        {
            return false;
        }

        var deleted = await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await _projectionUpdater.RemoveForDefinitionAsync(request.Id, ct);
                return await _eventCustomPropertyRepository.DeleteDefinition(request.Id, ct);
            },
            cancellationToken);

        if (!deleted)
        {
            return false;
        }

        await _cache.RemoveByTagAsync(
            EventCustomPropertyCache.ListsByEvent(definition.TenantId, definition.EventId),
            CancellationToken.None);

        return true;
    }
}
