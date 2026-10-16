namespace Explore.Application.Contracts.Persistence;

/// <summary>Exports bounded eligible tenant identities, never email content or mutation authority.</summary>
public interface IEmailRetentionTenantDiscoveryRepository
{
    Task<IReadOnlyList<Guid>> GetTenantIdsAsync(
        DateTime cutoffUtc, int maxTenants, CancellationToken cancellationToken);
}
