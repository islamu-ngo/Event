namespace Explore.Application.Contracts.Services;

public interface IEventAuthoritySnapshotService
{
    /// <summary>Protects event/actor ownership and contributing grant, role and permission rows until the caller commits.</summary>
    Task<EventAuthoritySnapshot> GetCommitBoundForUserAndEventsAsync(
        Guid tenantId, Guid userId, IReadOnlyCollection<Guid> eventIds,
        DateTime evaluationTimeUtc, CancellationToken cancellationToken);

    Task<EventAuthoritySnapshot> GetForUserAndEventsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> eventIds,
        DateTime evaluationTimeUtc,
        CancellationToken cancellationToken);
}

public sealed record EventAuthoritySnapshot(
    Guid TenantId,
    Guid UserId,
    IReadOnlyDictionary<Guid, EventAuthorityForUser> Events);

public sealed record EventAuthorityForUser(
    IReadOnlySet<string> RoleCodes,
    IReadOnlySet<string> PermissionCodes,
    bool IsOwner,
    bool IsManager);
