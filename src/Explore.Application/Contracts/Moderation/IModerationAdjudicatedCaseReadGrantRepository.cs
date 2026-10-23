using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>Creates current case permission, never allegation receipts or notification delivery evidence.</summary>
public interface IModerationAdjudicatedCaseReadGrantRepository
{
    Task<ModerationAdjudicatedCaseReadGrant?> GrantAsync(
        GrantModerationAdjudicatedCaseRead request, CancellationToken cancellationToken = default);
    Task<ModerationAdjudicatedCaseReadGrant?> RevokeAsync(
        Guid grantId, Guid operationId, long expectedRevision, CancellationToken cancellationToken = default);
    Task<ModerationAdjudicatedCaseReadGrant?> ReadCurrentAsync(
        Guid grantId, Guid caseId, long expectedCaseRevision, CancellationToken cancellationToken = default);
}

/// <summary>The original locator selects independently authenticated custody; it is not permission.</summary>
public sealed record GrantModerationAdjudicatedCaseRead(
    Guid OperationId, Guid CaseId, long ExpectedCaseRevision, Guid GranteeUserId,
    ModerationOriginalEffectAnchor Original, DateTimeOffset ValidUntil)
{
    public override string ToString() => nameof(GrantModerationAdjudicatedCaseRead);
}
