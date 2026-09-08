
using Explore.Application.Contracts.Identity;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

// Internal application messaging contract for the durable delivery worker, not a public API request body.
public sealed record ReconcileLocalIdentityLifecycleMirrorCommand(
    LocalIdentityLifecyclePointer Operation) : IRequest<BaseCommandResponse<Guid>>;

public sealed class ReconcileLocalIdentityLifecycleMirrorCommandHandler(
    ILocalIdentityLifecycleStore lifecycle, ISender sender, HybridCache cache)
    : IRequestHandler<ReconcileLocalIdentityLifecycleMirrorCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReconcileLocalIdentityLifecycleMirrorCommand request, CancellationToken cancellationToken)
    {
        bool synchronized = await lifecycle.ExecuteSynchronizationAsync(request.Operation,
            (current, token) => LocalIdentityLifecycleConsumptionOrchestrator.SynchronizeMirrorAsync(current, sender, token),
            cancellationToken);
        if (!synchronized) return BaseCommandResponse.Conflict(id: request.Operation.OperationId);
        await cache.RemoveAsync($"user:detail:{request.Operation.LocalSubjectId}", cancellationToken);
        return BaseCommandResponse.Success(id: request.Operation.OperationId);
    }
}
