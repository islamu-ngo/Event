using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class ApprovePublishEventCommandHandler(EventPublicationExecutor executor)
    : IRequestHandler<ApprovePublishEventCommand, BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> Handle(
        ApprovePublishEventCommand request,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync(
            request.Id,
            request.Request,
            EventPublicationMode.PrivilegedApproval,
            cancellationToken);
}
