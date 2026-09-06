// ABOUTME: Defines relational persistence operations for encrypted ATProto authentication transients.
// ABOUTME: Separates pre-tenant OAuth lookup from tenant-bound read and single-winner consumption.

using Explore.Domain;
namespace Explore.Application.Contracts.Persistence;

public interface IAtprotoTransientStoreRepository
{
    Task<bool> TryCreateAsync(AtprotoTransientRecord record, CancellationToken cancellationToken = default);
    Task<bool> TryCreateHealthProbeAsync(AtprotoTransientRecord record, CancellationToken cancellationToken = default);
    Task<AtprotoTransientRecord?> ReadHealthProbeAsync(Guid candidateId, string tokenDigest, CancellationToken cancellationToken = default);
    Task<AtprotoTransientRecord?> ReadOAuthStateAsync(string tokenDigest, CancellationToken cancellationToken = default);
    Task<AtprotoTransientRecord?> ReadAsync(AtprotoTransientPurpose purpose, string tokenDigest, Guid tenantId, CancellationToken cancellationToken = default);
    Task<AtprotoTransientRecord?> ConsumeAsync(Guid candidateId, AtprotoTransientPurpose purpose, string tokenDigest, Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> ConsumeHealthProbeAsync(Guid candidateId, string tokenDigest, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(long expiresAtOrBeforeUnixMilliseconds, int batchSize, CancellationToken cancellationToken = default);
}
