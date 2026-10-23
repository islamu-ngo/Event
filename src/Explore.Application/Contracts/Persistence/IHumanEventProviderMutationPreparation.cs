using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Prepares only the closed RegistrationProviders command set authorized as
/// Event/manage-registration-channels. Target identifiers come from the original immutable
/// command, never caller-authored authorization facts. Provider evaluation precedes transactions.
/// </summary>
public interface IHumanEventProviderMutationPreparation
{
    Task<IPreparedHumanEventProviderMutation> PrepareAsync(
        ICommand<BaseCommandResponse<Guid>> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes one previously prepared original attempt after remote work. It never
    /// reprepares or repeats that remote work after a rollback or unknown settlement.
    /// </summary>
    Task<T> ExecutePreparedAsync<T>(ICommand<BaseCommandResponse<Guid>> command,
        IPreparedHumanEventProviderMutation prepared, Func<CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares, enters the selected single-attempt owner, acquires the capability, then invokes
    /// database-only business work. Retries at most four complete preparations, and only after
    /// that owner's acknowledged rollback receipt. The callback must not start a transaction,
    /// invoke a remote provider, or translate admission failures into successful responses.
    /// </summary>
    Task<T> ExecuteAsync<T>(ICommand<BaseCommandResponse<Guid>> command,
        Func<CancellationToken, Task<T>> mutation, CancellationToken cancellationToken = default);
}

/// <summary>
/// Opaque, single-use, in-process capability bound to the original command instance, human
/// credential, selected owner, tenant, persisted Event and selected provider inputs.
/// Acquisition belongs inside the actual original Serializable attempt before business effects.
/// The owner must flush writes, complete all registered source reads, sample final trusted time
/// and commit without further blocking application work. A retry needs a new preparation.
/// </summary>
public interface IPreparedHumanEventProviderMutation
{
    /// <summary>Rechecks the original requester before acquisition and across remote awaits.</summary>
    Task ValidateAsync(ICommand<BaseCommandResponse<Guid>> originalCommand,
        CancellationToken cancellationToken = default);

    Task AcquireAsync(ICommand<BaseCommandResponse<Guid>> originalCommand,
        CancellationToken cancellationToken = default);
}
