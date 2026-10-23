using Explore.Application.Features.Settings;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Retains the complete original command, acquires its dedicated-setting source on
/// the original physical attempt, and verifies privately completed exact effects.
/// Command and subject arguments are data, not administrator authorization.
/// </summary>
public interface IAtprotoFederationSettingsMutationStore
{
    /// <summary>Observes the full ordinary command and its complete immutable effect envelope before BEGIN.</summary>
    Task<IAtprotoFederationSettingsSource> ObserveAsync(
        OrdinarySettingsMutation mutation,
        HumanCommitSubject subject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Observes the genuine full mixed command before BEGIN. Its accepted partition
    /// can be frozen only after the original active compiler produces it; candidate
    /// keys and future accepted effects are never evidence of completed writes.
    /// </summary>
    Task<IAtprotoFederationSettingsSource> ObserveAsync(
        MixedSettingsBatchMutation mutation,
        CancellationToken cancellationToken = default);

    Task AcquireAsync(IAtprotoFederationSettingsSource source,
        CancellationToken cancellationToken = default);

    Task VerifyAsync(IAtprotoFederationSettingsSource source,
        CancellationToken cancellationToken = default);

    Task RequireCompleteAsync(IAtprotoFederationSettingsSource source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates only local source custody. The original shared disclosure/gate
    /// owner separately releases physical resources asynchronously after settlement
    /// or failed preparation; this method cannot perform that cleanup.
    /// </summary>
    void Expire(IAtprotoFederationSettingsSource source);
}
