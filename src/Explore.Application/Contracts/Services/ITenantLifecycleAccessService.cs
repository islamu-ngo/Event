namespace Explore.Application.Contracts.Services;

/// <summary>Reads current lifecycle facts without caching public availability or administrative authority.</summary>
public interface ITenantLifecycleAccessService
{
    Task<bool> IsPublicAsync(Guid? tenantId, CancellationToken cancellationToken = default);
    Task<bool> CanManageAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
