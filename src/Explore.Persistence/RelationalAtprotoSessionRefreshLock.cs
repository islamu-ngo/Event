using System.Globalization;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;

namespace Explore.Persistence;

public sealed class RelationalAtprotoSessionRefreshLock(ExploreDbContext dbContext)
    : IAtprotoSessionRefreshLock
{
    public Task<IAsyncDisposable> AcquireAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("ATProto refresh scope is invalid.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectDid);
        return RelationalNamedLock.AcquireSessionAsync(
            dbContext,
            BuildResource(tenantId, userId, provider, subjectDid),
            cancellationToken);
    }

    internal static long ComputeStableLockKey(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid) =>
        RelationalNamedLock.ComputeStableKey(BuildResource(tenantId, userId, provider, subjectDid));

    private static string BuildResource(Guid tenantId, Guid userId, string provider, string subjectDid) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"explore:atproto-refresh:{tenantId:D}:{userId:D}:{provider}:{subjectDid}");
}
