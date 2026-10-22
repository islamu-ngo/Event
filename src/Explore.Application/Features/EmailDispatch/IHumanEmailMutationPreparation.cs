using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Requests.Commands;

namespace Explore.Application.Features.EmailDispatch;

/// <summary>
/// Private issuer-owned admission for one original Email owner and immutable effect.
/// Implementing this marker, holding a source lease or opening a transaction grants nothing.
/// </summary>
public interface IPreparedHumanEmailMutation;

public interface IHumanEmailMutationPreparation
{
    Task<MixedSettingsBatchMutation> PrepareMixedAsync(UpdateSettingBatchCommand command,
        CancellationToken cancellationToken = default);
    Task<T> ExecuteMixedPreparedAsync<T>(MixedSettingsBatchMutation mutation,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
    Task CloseMixedAsync(MixedSettingsBatchMutation mutation, Exception? primaryFailure);
    void RequireMixedSource(IHumanMixedSettingsSource source);

    Task<IPreparedHumanEmailMutation> PreparePreviewAsync(
        Guid? tenantId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanEmailMutation> PrepareApplyAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanEmailMutation> PrepareDisableAsync(
        EmailDeliveryDisableConfirmation confirmation, CancellationToken cancellationToken = default);

    // These overloads preserve the actual policy question, not a caller-selected allow delegate.
    Task<IPreparedHumanEmailMutation> PrepareAsync(SetInstanceTenantSettingCommand command, string storedValue,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanEmailMutation> PrepareAsync(LockInstanceTenantSettingCommand command,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanEmailMutation> PrepareAsync(UnlockInstanceTenantSettingCommand command,
        CancellationToken cancellationToken = default);

    Task<T> ExecutePreparedAsync<T>(IPreparedHumanEmailMutation preparation,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Direct entries prepare while idle; joined entries require the original acquired producer
    /// and exact effect. Instance-tenant writes always require the typed original policy owner.
    /// </summary>
    Task<T> ExecuteApplyAsync<T>(ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        bool instanceTenantAction, Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
    Task<T> ExecuteDisableAsync<T>(EmailDeliveryDisableConfirmation confirmation,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    Task<Guid> RequireApplyAsync(ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        bool instanceTenantAction, CancellationToken cancellationToken = default);
    Task<Guid> RequireDisableAsync(EmailDeliveryDisableConfirmation confirmation,
        CancellationToken cancellationToken = default);
    Task<Guid> RequireImpactAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
