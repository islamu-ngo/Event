using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ISupportAccessSessionRepository
{
    Task<SupportAccessSession> CreateAsync(SupportAccessSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(SupportAccessSession session, CancellationToken cancellationToken = default);

    Task<SupportAccessSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<SupportAccessSession?> GetActiveForActorAsync(
        Guid actorUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<SupportAccessSession?> GetActiveOwnedSessionAsync(
        Guid sessionId,
        Guid actorUserId,
        Guid? targetTenantId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<SupportAccessSession?> GetOwnedSessionAsync(
        Guid sessionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveSessionForActorAsync(Guid actorUserId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupportAccessSession>> ListForTargetTenantAsync(
        Guid targetTenantId,
        int limit,
        CancellationToken cancellationToken = default);
}
