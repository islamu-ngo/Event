using System.Collections.Immutable;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Native independent qualification under the already-held identity gate and serializable
/// primary transaction. No method opens, commits, rolls back or clears the caller's context.
/// Matching stages only new continuity entities; caller saves them with enrollment.
/// </summary>
public interface IModerationRecognitionContinuityRepository
{
    Task<ModerationRecognitionContinuityEvaluation> MatchAndBindAsync(
        ModerationRecognitionContinuityRequest request, CancellationToken token);
    Task<ModerationRecognitionContinuityFailure> GuardExistingSubjectAsync(Guid subjectId, CancellationToken token);
    Task<ModerationRecognitionContinuityEvaluation> ReadCurrentInTransactionAsync(Guid freshSubjectId, CancellationToken token);
    Task<ModerationRecognitionContinuityEvaluation> ReadPublicationInTransactionAsync(
        Guid freshSubjectId, AuthorityScope target, CancellationToken token);
    Task<ImmutableArray<ModerationRecognitionContinuityEvaluation>> ReadResourcesInTransactionAsync(
        Guid freshSubjectId, ModerationCapability capability, ImmutableArray<AuthorityScope> targets,
        CancellationToken token);
}
