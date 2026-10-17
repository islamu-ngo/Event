
using Explore.Application.Contracts.Identity;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local;

internal static class LocalCredentialAdministrator
{
    internal static async Task<ILocalAdministrationInvocation?> ResolveAsync(
        IAdminContext adminContext, ILocalAdministrationPreparation preparation, object originalRequest,
        CancellationToken cancellationToken)
    {
        Guid? userId = await adminContext.ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (userId is null || userId == Guid.Empty)
        {
            return null;
        }
        try
        {
            return await preparation.PrepareAsync(originalRequest, userId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static async Task<BaseCommandResponse<Guid>> ReconcileAsync(
        ILocalHumanCredentialAdministration administration, ILocalAdministrationInvocation invocation,
        object originalRequest, Guid operationId, CancellationToken cancellationToken)
    {
        var snapshot = await administration.ReadProvisioningAsync(invocation, originalRequest, cancellationToken);
        if (snapshot is null) return BaseCommandResponse.NotFound<Guid>(message: "Credential operation was not found.");
        if (snapshot.Receipt.Kind != LocalCredentialOperationKind.Create
            || snapshot.Receipt.Stage is not (LocalCredentialOperationStage.ProvisioningPending
                or LocalCredentialOperationStage.ChangeRequired))
            return BaseCommandResponse.Conflict(id: operationId);
        var binding = await administration.BindAsync(invocation, originalRequest, snapshot, cancellationToken);
        if (!binding.IsSuccess) return binding;
        var activation = await administration.ActivateAsync(invocation, originalRequest, snapshot, cancellationToken);
        return activation switch
        {
            LocalCredentialActivationOutcome.Activated or LocalCredentialActivationOutcome.AlreadyActivated =>
                BaseCommandResponse.Success(id: operationId),
            LocalCredentialActivationOutcome.NotFound => BaseCommandResponse.NotFound<Guid>(),
            LocalCredentialActivationOutcome.Conflict or LocalCredentialActivationOutcome.BindingIncomplete =>
                BaseCommandResponse.Conflict(id: operationId),
            _ => throw new InvalidOperationException("Unknown credential activation outcome.")
        };
    }

}
