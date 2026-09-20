
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

// Internal application messaging contract for the durable delivery worker, not a public API request body.
public sealed record ReconcileLocalIdentityLifecycleMirrorCommand(
    LocalIdentityLifecyclePointer Operation) : ICommand<BaseCommandResponse<Guid>>;

public sealed class ReconcileLocalIdentityLifecycleMirrorCommandHandler(
    ILocalIdentityLifecycleStore lifecycle,
    ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>> syncUserCommandHandler,
    HybridCache cache)
    : ICommandHandler<ReconcileLocalIdentityLifecycleMirrorCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ReconcileLocalIdentityLifecycleMirrorCommand request, CancellationToken cancellationToken = default)
    {
        bool synchronized = await lifecycle.ExecuteSynchronizationAsync(request.Operation,
            (current, token) => LocalIdentityLifecycleConsumptionOrchestrator.SynchronizeMirrorAsync(current, syncUserCommandHandler, token),
            cancellationToken);
        if (!synchronized) return BaseCommandResponse.Conflict(id: request.Operation.OperationId);
        await cache.RemoveAsync($"user:detail:{request.Operation.LocalSubjectId}", cancellationToken);
        return BaseCommandResponse.Success(id: request.Operation.OperationId);
    }
}
