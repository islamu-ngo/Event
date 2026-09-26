namespace Explore.Application.Contracts.Services;

/// <summary>Reads current lifecycle facts without caching public availability or administrative authority.</summary>
public interface ITenantLifecycleAccessService
{
    Task<bool> IsPublicAsync(Guid? tenantId, CancellationToken cancellationToken = default);
    Task<bool> CanManageAsync(Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Allows only an attempt at configured bootstrap on the provisioning default tenant.
    /// The synchronization handler must still validate the exact account and current bootstrap generation.
    /// </summary>
    Task<bool> CanAttemptConfiguredAdministratorSyncAsync(Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Resolves only the explicit default-tenant slug for eligible private configured-bootstrap session endpoints.</summary>
    Task<Guid?> ResolveConfiguredAdministratorTenantAsync(string tenantSlug, CancellationToken cancellationToken = default);
}
