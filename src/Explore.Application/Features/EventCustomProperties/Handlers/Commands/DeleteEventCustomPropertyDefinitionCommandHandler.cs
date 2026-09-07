using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Commands;

public class DeleteEventCustomPropertyDefinitionCommandHandler : IRequestHandler<DeleteEventCustomPropertyDefinitionCommand, bool>
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

    public async Task<bool> Handle(DeleteEventCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
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

        await _cache.RemoveAsync(
            $"event-custom-properties:list:{definition.EventId}:1:{PaginatedResult<object>.DefaultPageSize}",
            cancellationToken);
        await _cache.RemoveAsync($"event-custom-properties:detail:{definition.Id}", cancellationToken);

        return true;
    }
}
