using System.Collections.Immutable;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain.Settings;
using Explore.Application.Settings;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

public enum HumanSettingsMutationKind { SetValue, SetLock, Remove }
public enum HumanSettingsOperation { UserSettings, TenantSettings, InstanceSettings, InstanceTenantSettings }

/// <summary>
/// Exact storage effect, not caller identity or permission. Values are the already validated,
/// serialized values the owning handler will persist. Null tenant denotes an instance owner;
/// a User effect additionally binds the original resolved user.
/// </summary>
public sealed record HumanSettingsMutation(HumanSettingsOperation Operation, SettingScope Scope, Guid? TenantId, Guid? UserId,
    string Key, HumanSettingsMutationKind Kind, string? Value = null, bool? IsLocked = null);

/// <summary>
/// Opaque in-process capability. Implementing this interface does not create an admission.
/// Only its scoped issuer can execute it or admit a public data operation with it.
/// </summary>
public interface IPreparedHumanSettingsMutation;

/// <summary>
/// Prepare outside the original Runtime/Global owner's transaction, after choosing that owner.
/// Existing handlers retain value validation, serialization, registry rules and ordered locks.
/// No method accepts a policy name, an allow callback, a lease, or an import/human flag.
/// </summary>
public interface IHumanSettingsMutationPreparation
{
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateLocalizationGovernanceCommand command,
        CancellationToken cancellationToken = default);
    /// <summary>The ordinary source of this issuer's active callback, never an import or mixed source.</summary>
    IOrdinarySettingsSource? CurrentOrdinarySource { get; }
    /// <summary>The dedicated source of this issuer's active original or joined callback.</summary>
    IAtprotoFederationSettingsSource? CurrentAtprotoFederationSource { get; }
    TypedSettingsOriginal? CurrentTypedOriginal { get; }
    EventResourceProviderOriginal? CurrentEventResourceProviderOriginal { get; }
    void RequireEventResourceProviderSource(IEventResourceProviderOriginalSource source);
    /// <summary>Closes an inactive original preparation, including failures before its owner starts.</summary>
    Task CloseAsync(IPreparedHumanSettingsMutation prepared, Exception? primaryFailure);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(BindEventResourceProviderCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(BeginEventResourceProviderOperationCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(ActivateEventResourceProviderCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(SyncAuthorizationPolicyPackageCommand command,
        CancellationToken cancellationToken = default);
    void RequireTypedSource(IHumanMixedSettingsSource source);
    Task<IPreparedHumanTenantPlanAssignment> PrepareAsync(ApplyInstanceTenantPlanAssignmentCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateEventPolicyCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateModuleSettingsCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateOrganizationPolicyCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateBrandingSettingsCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateDomainSettingsCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateTenantDelegationSettingsCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAdminPortalSettingsCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateMcpGovernanceSettingsCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAiAssistantGovernanceSettingsCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateRenderPolicySettingsCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(CompleteTenantOnboardingCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateTenantReportingIntakePolicyCommand command,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteMixedJoinedAsync<T>(MixedSettingsBatchMutation mutation,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateResolverConfigurationCommand command,
        ResolverConfigurationMutation mutation, CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(SaveInstanceOperatorIdentityCommand command,
        OperatorIdentityMutation mutation, CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(ImportInstanceOperatorIdentityCommand command,
        OperatorIdentityMutation mutation, CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateInstanceStorageSettingsCommand command,
        StorageSettingsMutation mutation, CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(PatchTenantStorageSettingsCommand command,
        StorageSettingsMutation mutation, CancellationToken cancellationToken = default);
    Task<T> ExecuteStorageJoinedAsync<T>(IPreparedHumanSettingsMutation owner,
        StorageSettingsMutation storage, Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAuthProviderConfigurationCommand command,
        ProviderConfigurationMutation mutation, CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAuthorizationProviderConfigurationCommand command,
        ProviderConfigurationMutation mutation, CancellationToken cancellationToken = default);
    Task<T> ExecuteProviderJoinedAsync<T>(IPreparedHumanSettingsMutation owner,
        ProviderConfigurationMutation provider, Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateSettingCommand command, string storedValue,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateSettingBatchCommand command,
        ImmutableDictionary<string, string> storedValues, CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(ResetSettingCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(LockSettingCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UnlockSettingCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(SetInstanceTenantSettingCommand command, string storedValue,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(LockInstanceTenantSettingCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UnlockInstanceTenantSettingCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareResourceAsync(ImmutableArray<EventResourceSettingMutation> mutations,
        SettingsMutationOperation operation, Guid? actorUserId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareVisitorAsync(ImmutableArray<VisitorAccessSettingMutation> mutations,
        SettingsMutationOperation operation, Guid? actorUserId, CancellationToken cancellationToken = default);

    Task<T> ExecutePreparedAsync<T>(IPreparedHumanSettingsMutation prepared,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Borrows only this issuer's active original owner; the child proof ends when the callback
    /// ends. Its result remains provisional until the enclosing owner commits. Import joining
    /// requires the import producer's own issuer-checked effect seam, not its public Acquire port.
    /// </summary>
    Task<T> ExecuteJoinedAsync<T>(IPreparedHumanSettingsMutation owner, ImmutableArray<HumanSettingsMutation> slice,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);
    Task<T> ExecuteCurrentJoinedAsync<T>(ImmutableArray<HumanSettingsMutation> slice,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Derives publication values and paired locks from the original command and acquired
    /// source rows, including removal of a locked tenant override when the boundary
    /// selects that existing cleanup behavior. The callback cannot supply additional effects; support remains the
    /// original command's action. The derived capability expires with the callback.
    /// </summary>
    Task<T> ExecutePublicationJoinedAsync<T>(ImmutableArray<string> keys, Guid? tenantId, Guid actorUserId,
        PublicationPolicyLockedSystemBehavior lockedSystemBehavior,
        Func<ImmutableArray<PublicationPolicySettingMutation>, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);
    /// <summary>Admits only the fixed publication snapshot for the original target.</summary>
    Task<HumanSettingsMutation> RequirePublicationReadAsync(Guid? tenantId,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Narrows to the original location effect. Reset remains Remove; its effective value
    /// comes from the acquired instance row or registry default, never a caller's fallback.
    /// </summary>
    Task<T> ExecuteLocationJoinedAsync<T>(string key, SettingScope scope, Guid? tenantId, Guid actorUserId,
        Func<IPreparedHumanSettingsMutation, HumanSettingsMutation, string, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);

    Task RequireOwnedAsync(IPreparedHumanSettingsMutation prepared, HumanSettingsMutation mutation,
        CancellationToken cancellationToken = default);
    Task RequireCurrentOwnedAsync(SettingScope scope, Guid? tenantId, Guid? userId, string key,
        HumanSettingsMutationKind kind, string? value = null, bool? isLocked = null,
        CancellationToken cancellationToken = default);
    Task RequireReadAsync(IPreparedHumanSettingsMutation prepared, SettingScope scope, Guid? tenantId,
        Guid? userId, string key, CancellationToken cancellationToken = default);
}
