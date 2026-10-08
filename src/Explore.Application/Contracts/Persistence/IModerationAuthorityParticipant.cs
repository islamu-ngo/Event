using ISLAMU.Moderation.Application;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Joins the native authority writers to the store-owned transaction before resolving permission.</summary>
public interface IModerationAuthorityParticipant : IModerationAuthorityContext
{
    /// <summary>
    /// Acquires actual jurisdiction and qualification source fences, then subject and policy fences.
    /// Reads use the acquisition connection and every fence remains owned until transaction completion.
    /// Identity-changing work must already own its privacy boundary before acquiring subordinate fences.
    /// </summary>
    /// <remarks>
    /// Revision numbers, request identifiers and unrelated named locks are not qualification evidence.
    /// The implementation shares the store's scoped primary context and must fence every source used
    /// by ResolveAsync and QualifyActivationAsync, including independent reviewers.
    /// </remarks>
    Task<ModerationAuthorityResolution> AcquireAsync(
        IModerationSession session, ModerationOperation operation, CancellationToken cancellationToken);
}
