namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Holds the immutable authenticated user's live identity and exact provider binding
/// in the original Serializable owner, without requiring an administrator grant.
/// </summary>
public interface IHumanSubjectCommitAuthority
{
    Task<bool> IsSubjectCurrentForCommitAsync(Guid principalId, CancellationToken cancellationToken);
}
