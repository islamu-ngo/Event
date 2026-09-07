using Explore.Application.Authorization;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Service that triggers authorization policy package publishing after role mutations.
/// Concrete transport, endpoint resolution, and provider-specific upload behavior stay in Infrastructure.
/// </summary>
public interface IPolicySyncService
{
    /// <summary>
    /// Publishes the current authorization policy package.
    /// </summary>
    Task SyncAllPoliciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the current authorization policy package after a role mutation.
    /// </summary>
    /// <param name="roleId">The role that was created, updated, or deleted.</param>
    Task SyncRolePoliciesAsync(int roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-publishes the current authorization policy package so providers can reload their store.
    /// </summary>
    Task ReloadAllInstancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes a summary of the current policy package without publishing it.
    /// </summary>
    Task<PolicyPackageInfo> GetPolicySummaryAsync(CancellationToken cancellationToken = default);
}
