using System;
using System.Threading;
using System.Threading.Tasks;

namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Provides the effective lifecycle validation policy for a given profile,
/// composed from hard invariants and optional tenant/instance configuration.
/// </summary>
public interface IEventLifecyclePolicyProvider
{
    /// <summary>
    /// Returns the effective <see cref="EventLifecyclePolicy"/> for the given profile and tenant.
    /// </summary>
    /// <param name="tenantId">Optional tenant id for tenant-scoped overrides; null uses instance defaults.</param>
    /// <param name="profile">The validation profile to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EventLifecyclePolicy> GetEffectivePolicyAsync(
        Guid? tenantId,
        ValidationProfile profile,
        CancellationToken cancellationToken);
}
