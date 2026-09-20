using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCustomPropertyProjections.Handlers.Commands;

public class RebuildSingleEventCustomPropertyProjectionCommandHandler
    : ICommandHandler<RebuildSingleEventCustomPropertyProjectionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly IUnitOfWork _unitOfWork;

    public RebuildSingleEventCustomPropertyProjectionCommandHandler(
        IEventCustomPropertyProjectionUpdater projectionUpdater,
        IUnitOfWork unitOfWork)
    {
        _projectionUpdater = projectionUpdater;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RebuildSingleEventCustomPropertyProjectionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.EventId == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(["EventId is required."], "EventId is required.");
        }

        await _unitOfWork.ExecuteInTransactionAsync(
            async ct => await _projectionUpdater.RefreshForEventAsync(request.EventId, ct),
            cancellationToken);

        return BaseCommandResponse.Success(request.EventId, "Event projection rows refreshed successfully.");
    }
}
