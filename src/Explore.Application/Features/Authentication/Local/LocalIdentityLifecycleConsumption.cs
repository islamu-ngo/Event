
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Authentication.Local;

internal static class LocalIdentityLifecycleConsumptionOrchestrator
{
    internal static async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        LocalIdentityLifecycleConsumption consumption,
        ILocalIdentityLifecycleStore lifecycle,
        ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>> syncUserCommandHandler,
        HybridCache cache,
        CancellationToken cancellationToken)
    {
        bool mirrorAttempted = false;
        async Task<bool> SynchronizeAsync(LocalIdentityLifecycleSynchronization current, CancellationToken token)
        {
            mirrorAttempted = true;
            return await SynchronizeMirrorAsync(current, syncUserCommandHandler, token);
        }

        // A valid consumed receipt is synchronized before attempting any credential operation. Public retry
        // must retain the original native token; pointer knowledge alone never authorizes this callback.
        bool complete = await lifecycle.ExecuteSynchronizationAsync(consumption, SynchronizeAsync, cancellationToken);
        if (!complete && !mirrorAttempted)
        {
            LocalIdentityLifecycleResult consumed = await lifecycle.ConsumeAsync(consumption, cancellationToken);
            if (consumed.Outcome != LocalIdentityLifecycleOutcome.Consumed)
            {
                return consumed.Outcome == LocalIdentityLifecycleOutcome.Conflict
                    ? BaseCommandResponse.Conflict(id: consumption.Operation.OperationId)
                    : BaseCommandResponse.Validation<Guid>(errors: ["The operation or proposed password could not be accepted."]);
            }
            // Never apply ConsumeAsync's snapshot: the store reacquires current native authority and exact
            // application binding under the shared mutation serialization before invoking SyncUser.
            complete = await lifecycle.ExecuteSynchronizationAsync(consumption, SynchronizeAsync, cancellationToken);
        }
        if (!complete)
            return BaseCommandResponse.Conflict(id: consumption.Operation.OperationId,
                message: "The Local profile could not be synchronized. Retry the original operation.");
        await cache.RemoveAsync($"user:detail:{consumption.Operation.LocalSubjectId}", cancellationToken);
        return BaseCommandResponse.Success(id: consumption.Operation.OperationId);
    }

    // Called only inside either core-owned synchronization transaction, never from a public pointer reader.
    internal static async Task<bool> SynchronizeMirrorAsync(
        LocalIdentityLifecycleSynchronization current,
        ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>> syncUserCommandHandler,
        CancellationToken cancellationToken)
    {
        if (current.Synchronized) return true;
        var key = new ProviderAccountKey(AuthenticationProviderKind.Local, current.ApplicationUserId.ToString("D"));
        BaseCommandResponse<Guid> synchronized = await syncUserCommandHandler.ExecuteAsync(new SyncUserCommand
        {
            AccountKey = key,
            LocalLifecycleSynchronization = current,
            UserDto = new UserDto
            {
                Id = current.ApplicationUserId,
                AuthProvider = "local",
                AuthProviderId = key.Value,
                Email = current.Email ?? string.Empty,
                EmailVerified = current.EmailVerified,
                FirstName = current.FirstName,
                LastName = current.LastName
            }
        }, cancellationToken);
        return synchronized.IsSuccess && synchronized.Id == current.ApplicationUserId;
    }
}
