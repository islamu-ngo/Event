using Explore.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>Administrative intent for one finite material read purpose, not current caller authority.</summary>
public sealed record ModerationCaseReadGrantRequest(
    Guid OperationId, Guid CaseId, long ExpectedCaseRevision, Guid SourceId,
    Guid CallerUserId, DateTimeOffset ValidUntil)
{
    public override string ToString() => nameof(ModerationCaseReadGrantRequest);
}

/// <summary>Commits native read-grant changes under current human authority and immutable operation ownership.</summary>
public interface IModerationCaseReadGrantRepository
{
    Task<ModerationCaseReadGrant?> GrantAsync(
        ModerationCaseReadGrantRequest request, CancellationToken cancellationToken);
    Task<ModerationCaseReadGrant?> RevokeAsync(
        Guid grantId, Guid operationId, long expectedRevision, CancellationToken cancellationToken);
}
