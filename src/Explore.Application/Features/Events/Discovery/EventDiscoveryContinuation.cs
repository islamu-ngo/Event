namespace Explore.Application.Features.Events.Discovery;

/// <summary>Authenticated traversal state; expiry and authority are checked against the current database view.</summary>
public sealed record EventDiscoveryContinuation(
    Guid TenantId,
    Guid SnapshotId,
    string CriteriaHash,
    long NextOrdinal,
    DateTimeOffset ExpiresAtUtc,
    long IdentityEpoch,
    long DisclosureEpoch);

public interface IEventDiscoveryCursorProtector
{
    string Protect(EventDiscoveryContinuation continuation);
    EventDiscoveryContinuation Unprotect(string cursor);
}
