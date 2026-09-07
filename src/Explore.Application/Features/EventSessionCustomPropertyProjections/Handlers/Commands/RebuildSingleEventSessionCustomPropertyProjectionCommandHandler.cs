using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomPropertyProjections.Handlers.Commands;

public class RebuildSingleEventSessionCustomPropertyProjectionCommandHandler
    : IRequestHandler<RebuildSingleEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly IUnitOfWork _unitOfWork;

    public RebuildSingleEventSessionCustomPropertyProjectionCommandHandler(
        IEventSessionCustomPropertyProjectionUpdater projectionUpdater,
        IUnitOfWork unitOfWork)
    {
        _projectionUpdater = projectionUpdater;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        RebuildSingleEventSessionCustomPropertyProjectionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.EventSessionId == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(["EventSessionId is required."], "EventSessionId is required.");
        }

        await _unitOfWork.ExecuteInTransactionAsync(
            async ct => await _projectionUpdater.RefreshForEventSessionAsync(request.EventSessionId, ct),
            cancellationToken);

        return BaseCommandResponse.Success(request.EventSessionId, "Event session projection rows refreshed successfully.");
    }
}
