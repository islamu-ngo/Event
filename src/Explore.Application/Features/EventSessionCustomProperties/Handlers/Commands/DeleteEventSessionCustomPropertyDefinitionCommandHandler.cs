using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;

public class DeleteEventSessionCustomPropertyDefinitionCommandHandler : IRequestHandler<DeleteEventSessionCustomPropertyDefinitionCommand, bool>
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

    public async Task<bool> Handle(DeleteEventSessionCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
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

        await _cache.RemoveAsync(
            $"session-custom-properties:list:{definition.EventSessionId}:1:{PaginatedResult<object>.DefaultPageSize}",
            cancellationToken);
        await _cache.RemoveAsync($"session-custom-properties:detail:{definition.Id}", cancellationToken);

        return true;
    }
}
