using System.Collections.Immutable;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Read-only current contribution qualification on an existing serializable primary tenant
/// transaction. No identity gate, setting lock, identifier recomputation or row-write fence
/// is acquired: terminal consumers may call after their existing resource authority locks.
/// The native snapshot rechecks original finding/sanction/source/receipt/settlement authority.
/// Bindings alone are never enforcement permission; evaluate exact action/scope at the consumer.
/// </summary>
public interface IModerationRecognitionContinuityReader
{
    Task<ModerationRecognitionContinuityResult> ReadCurrentInTransactionAsync(Guid freshSubjectId, CancellationToken token);

    /// <summary>
    /// Qualifies only original Publish contributions for the exact current tenant target.
    /// Applicable unresolved recovery fails closed. Uses the caller's existing serializable
    /// transaction without identity/key access or new locks; a successful read grants no access.
    /// </summary>
    Task<ModerationRecognitionContinuityResult> ReadPublicationInTransactionAsync(
        Guid freshSubjectId, AuthorityScope target, CancellationToken token);

    /// <summary>
    /// Qualifies only ReadResource or Upload contributions whose original scope contains each requested
    /// resource in the current tenant and host. Results preserve target order and duplicates.
    /// An applicable unknown closes only its targets; a subject/native boundary failure closes
    /// every target. At most EventResourceAuthorityRequest.MaximumBatchResources are accepted.
    /// Empty batches return no results. Neither a binding nor a successful read grants access.
    /// </summary>
    Task<ImmutableArray<ModerationRecognitionContinuityResult>> ReadResourcesInTransactionAsync(
        Guid freshSubjectId, ModerationCapability capability, ImmutableArray<AuthorityScope> targets,
        CancellationToken token);
}
