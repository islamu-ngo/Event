using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Provides async-safe deployment mode resolution from config, cache, or database.
/// All consumers (middleware, filters, handlers) use this single interface.
/// Registered as Singleton; uses IServiceScopeFactory internally for scoped DB access.
/// </summary>
public interface IDeploymentModeProvider
{
    /// <summary>
    /// Returns the current effective deployment mode.
    /// Before onboarding completes this deliberately returns SingleTenant so setup endpoints
    /// can run without tenant host resolution; after onboarding it returns persisted runtime mode.
    /// </summary>
    Task<DeploymentMode> GetCurrentModeAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the deployment mode configured for first-run onboarding.
    /// Source: DEPLOYMENT_MODE / Deployment:Mode static config; default SingleTenant.
    /// </summary>
    Task<DeploymentMode> GetConfiguredOnboardingModeAsync(CancellationToken ct = default);

    /// <summary>Returns true when the current mode is SingleTenant.</summary>
    Task<bool> IsSingleTenantAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes the cached mode. The next call re-reads from the database.
    /// Call this after a deployment mode change has been committed.
    /// </summary>
    Task InvalidateCacheAsync();
}
