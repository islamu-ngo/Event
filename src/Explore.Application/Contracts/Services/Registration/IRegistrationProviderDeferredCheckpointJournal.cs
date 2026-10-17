using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services.Registration;

/// <summary>
/// Retains remote business observations outside the original human transaction. This journal
/// grants no authority and provides no commit or rollback receipt. Register one scoped instance
/// shared by the transport checkpoint sink and the human consumer after owner selection.
/// </summary>
public interface IRegistrationProviderDeferredCheckpointJournal
{
    Task<IRegistrationProviderCheckpointObservation<T>> ObserveAsync<T>(
        HumanEventProviderSourceTarget target,
        IPreparedHumanEventProviderMutation prepared,
        Func<CancellationToken, Task<T>> observe,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes the original observation through its original preparation issuer's single-attempt
    /// owner. Applies only local checkpoint effects after capability acquisition, before invoking
    /// the database-only callback and final source/time admission. Never replays remote work.
    /// </summary>
    Task<TResult> ExecuteAsync<T, TResult>(
        ICommand<BaseCommandResponse<Guid>> command,
        IPreparedHumanEventProviderMutation prepared,
        IRegistrationProviderCheckpointObservation<T> observation,
        Func<T, CancellationToken, Task<TResult>> mutation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called only by the connection checkpoint service with its trusted observation time.
    /// False selects the separate immediate worker path. A human owner without active observation,
    /// an unknown effect or a different tenant/connection fails rather than selecting that path.
    /// </summary>
    bool TryDefer(Guid tenantId, Guid connectionId, RegistrationProviderCheckpointEffect effect,
        DateTime observedAtUtc);
}

/// <summary>
/// Opaque issuer-owned, single-use custody. Result is provider business data, never authority.
/// Implementing this interface does not create an observation accepted by the journal.
/// </summary>
public interface IRegistrationProviderCheckpointObservation<out T>
{
    T Result { get; }
}

public enum RegistrationProviderCheckpointEffect
{
    AccessValidated = 1,
    CredentialRefreshed = 2
}
