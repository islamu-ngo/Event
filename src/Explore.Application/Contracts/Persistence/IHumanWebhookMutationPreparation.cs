using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Prepares only the closed HumanWebhookResourceTarget command catalog. Resource, action,
/// principal and ownership are derived from the original command and persisted sources.
/// Preparation performs one selected-provider evaluation outside every transaction.
/// </summary>
public interface IHumanWebhookMutationPreparation
{
    Task<IPreparedHumanWebhookMutation> PrepareAsync(
        ISecureRequest command, CancellationToken cancellationToken = default);

    Task<IPreparedHumanWebhookCancellation> PrepareCancellationAsync(
        CancelWebhookBulkReplayCommand command, CancellationToken cancellationToken = default);

    Task<IPreparedHumanWebhookRepairRequester> PrepareRepairAsync(
        RepairWebhookProviderBindingCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs database-only work after acquisition in the selected original Serializable owner.
    /// The callback must flush its writes and preserve the command's exact target lineage.
    /// It must not open another owner, call a remote provider or swallow admission failures.
    /// Only the actual owner's acknowledged rollback permits whole-attempt re-preparation.
    /// OpenPortal uses PreparePortalAsync, never this transaction wrapper.
    /// </summary>
    Task<T> ExecuteAsync<T>(ISecureRequest command, Func<CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets a selected scheduler acquire the original capability inside this owner.
    /// Completed acquisition is mandatory before return; settlement and re-preparation
    /// stay with the original owner rather than the scheduler.
    /// </summary>
    Task<T> ExecuteAsync<T>(ISecureRequest command,
        Func<IPreparedHumanWebhookMutation, CancellationToken, Task<T>> acquireAndMutate,
        CancellationToken cancellationToken = default);

    Task<IPreparedHumanWebhookPortalRequester> PreparePortalAsync(
        OpenSvixAppPortalCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Opaque, private-implementation, single-use command-instance capability. Acquire before any
/// business write in the real original Serializable owner. Acquisition registers final fresh
/// P/route/H/W reads and last nonblocking authentic credential/support admission with that owner.
/// Unchanged targets permit rejection/no-op responses; changed targets must match the closed
/// command-specific transition, never an arbitrary caller-authored state or authority tuple.
/// Created rows not selected by the original W selector still need the caller's typed lineage checks.
/// </summary>
public interface IPreparedHumanWebhookMutation
{
    Task AcquireAsync(ISecureRequest originalCommand, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repair-only original requester custody. Consumer and bindings are immutable INITIAL source
/// observations, available after genuine preparation for exact remote-proof input matching;
/// they are not current reads, settlement witnesses or authority to persist evidence.
/// Validate the same command outside transactions immediately before and after remote ownership
/// verification. Acquire exactly once in the actual owner, before mutation or rejected audit.
/// Acquisition and final admission retain the original selected P/route/H/W and credential.
/// No checkpoint is available after acquisition and no retry or rollback evidence is minted here.
/// </summary>
public interface IPreparedHumanWebhookRepairRequester : IPreparedHumanWebhookMutation
{
    Guid PrincipalId { get; }
    HumanWebhookConsumerFact Consumer { get; }
    ImmutableArray<HumanWebhookBindingFact> Bindings { get; }
    Task ValidateAsync(RepairWebhookProviderBindingCommand originalCommand,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Cancellation-only original requester custody. The acquired immutable operation is source
/// evidence, not body attribution or a rejection certificate. Reprepare only after consuming
/// the actual owner's whole-rollback witness; this capability never certifies settlement.
/// Evidence admission retains the original credential and exact W, permitting no W transition.
/// </summary>
public interface IPreparedHumanWebhookCancellation : IPreparedHumanWebhookMutation
{
    Guid PrincipalId { get; }
    /// <summary>Null means acquired absence in the independently observed existing selected tenant.</summary>
    HumanWebhookBulkReplayFact? Operation { get; }
    Task<IPreparedHumanWebhookMutation> PrepareEvidenceAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reusable requester checkpoint, NOT token-release authority or provider eligibility.
/// Retains the original human, selected decision semantics, consumer and binding custody.
/// Validate outside transactions before the first remote call, between remote awaits,
/// after the remote result, after handler checks and after audit persistence.
/// The portal owner must ALSO retain its provider/options/binding checks and add a final
/// release boundary joining these observations. No remote token or I/O belongs under P/H/W.
/// A successful checkpoint cannot promise atomic authorization of a later remote release.
/// </summary>
public interface IPreparedHumanWebhookPortalRequester
{
    WebhookPortalConsumerCustody Consumer { get; }
    Task ValidateAsync(OpenSvixAppPortalCommand originalCommand, CancellationToken cancellationToken = default);

    /// <summary>
    /// Joins provider reads with fresh original P/route/H/W observations. All blocking
    /// reads finish before the authentic credential/support clock sample. The last
    /// callback must be synchronous: it checks retained provider/options/result expiry,
    /// not human authority, and must not start remote work or acquire source leases.
    /// This is sequential observation, not an atomic authorization of remote issuance.
    /// </summary>
    Task ValidateAsync(OpenSvixAppPortalCommand originalCommand,
        Func<CancellationToken, Task> readProvider,
        Action<DateTimeOffset> validateProviderAtRelease,
        CancellationToken cancellationToken = default);
}
