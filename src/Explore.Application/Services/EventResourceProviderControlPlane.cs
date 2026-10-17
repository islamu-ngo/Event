using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Settings;

namespace Explore.Application.Services;

public sealed record EventResourceProviderOperation(Guid DeploymentId, Guid OperationId, long Epoch, Guid BindingRevision);

/// <summary>
/// Retains original commands through the selected human owner. The ordered binding lock
/// precedes the single Serializable attempt and ends before a publication target escapes.
/// </summary>
public sealed class EventResourceProviderControlPlane(
    ISystemSettingRepository settings,
    ISettingMutationLock mutationLock,
    IAdminContext adminContext,
    IHumanSettingsMutationPreparation preparation,
    IEventResourceProviderOriginalStore store) : IEventResourcePolicyPublicationFence
{
    public async Task<EventResourceProviderBindingDocument> ReadBindingsAsync(CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(cancellationToken);
        return EventResourceProviderBindingDocument.Parse((await settings.GetByKey(
            EventResourceProviderBindingDocument.SettingKey, cancellationToken))?.Value);
    }

    public async Task<EventResourceProviderOperation> BindAsync(
        BindEventResourceProviderCommand command, CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(cancellationToken);
        var prepared = await preparation.PrepareAsync(command, cancellationToken);
        return await ExecuteAsync(prepared, (original, token) => store.ApplyBindAsync(original, token), cancellationToken);
    }

    public async Task<EventResourceProviderOperation> BeginAsync(
        BeginEventResourceProviderOperationCommand command, CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(cancellationToken);
        var prepared = await preparation.PrepareAsync(command, cancellationToken);
        return await ExecuteAsync(prepared, (original, token) => store.ApplyBeginAsync(original, token), cancellationToken);
    }

    public async Task<bool> ActivateAsync(ActivateEventResourceProviderCommand command, CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(cancellationToken);
        var prepared = await preparation.PrepareAsync(command, cancellationToken);
        return await ExecuteAsync(prepared, (original, token) => store.ApplyActivateAsync(original, token), cancellationToken);
    }

    public async Task<IEventResourceProviderPublicationSelection> BeginPublicationAsync(
        SyncAuthorizationPolicyPackageCommand command, CancellationToken cancellationToken)
    {
        var prepared = await preparation.PrepareAsync(command, cancellationToken);
        return await ExecuteAsync(prepared, (original, token) => store.ApplyPublicationAsync(original, token), cancellationToken);
    }

    private async Task<T> ExecuteAsync<T>(IPreparedHumanSettingsMutation prepared,
        Func<EventResourceProviderOriginal, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await mutationLock.ExecuteOrderedGroupsAsync([[EventResourceProviderBindingDocument.SettingKey]],
                token => preparation.ExecutePreparedAsync(prepared, (_, selectedToken) => action(
                    preparation.CurrentEventResourceProviderOriginal
                        ?? throw new UnauthorizedAccessException("The original resource-provider owner is required."),
                    selectedToken), token), cancellationToken);
        }
        finally { preparation.CloseEventResourceProvider(prepared); }
    }

    private async Task RequireAdministratorAsync(CancellationToken token)
    {
        if (!await adminContext.IsInstanceAdminAsync(token))
            throw new AuthorizationException("Resource provider control-plane operations require current instance administrator authority.");
    }
}
