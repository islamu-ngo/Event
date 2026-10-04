namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Reads a conservative future clock boundary for all public source candidates,
/// including candidates omitted by the current response's filters or page.
/// </summary>
public interface IEventDiscoveryResponseBoundaryRepository
{
    Task<DateTimeOffset?> GetNextAsync(
        Guid tenantId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken);
}
