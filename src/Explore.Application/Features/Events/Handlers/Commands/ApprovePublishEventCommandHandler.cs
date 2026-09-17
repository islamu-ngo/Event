using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class ApprovePublishEventCommandHandler(EventPublicationExecutor executor)
    : ICommandHandler<ApprovePublishEventCommand, BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ApprovePublishEventCommand request,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync(
            request.Id,
            request.Request,
            EventPublicationMode.PrivilegedApproval,
            cancellationToken);
}
