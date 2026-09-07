using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Compatibility facade for callers that still request policy synchronization after role changes.
/// All publish behavior is delegated to <see cref="IPolicyPackageService"/> so there is only one
/// Cerbos Admin API writer, endpoint resolver, and redaction boundary.
/// </summary>
public sealed class PolicySyncService : IPolicySyncService
{
    private readonly IPolicyPackageService _policyPackageService;
    private readonly ILogger<PolicySyncService> _logger;

    public PolicySyncService(
        IPolicyPackageService policyPackageService,
        ILogger<PolicySyncService> logger)
    {
        _policyPackageService = policyPackageService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SyncAllPoliciesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publishing authorization policy package through the consolidated package publisher");
        await PublishPackageAsync("full policy sync", cancellationToken);
    }

    /// <inheritdoc />
    public async Task SyncRolePoliciesAsync(int roleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Role {RoleId} changed; publishing authorization policy package through the consolidated package publisher",
            roleId);

        await PublishPackageAsync($"role {roleId} policy sync", cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReloadAllInstancesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reload requested; publishing authorization policy package through the consolidated package publisher");
        await PublishPackageAsync("reload request", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PolicyPackageInfo> GetPolicySummaryAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await _policyPackageService.BuildManifestAsync(cancellationToken);

        var policyCount = manifest.Artifacts.Count(artifact => artifact.Kind == PolicyArtifactKind.Policy);

        return new PolicyPackageInfo(
            RoleCount: 0,
            PolicyCount: policyCount,
            TotalPermissionCount: 0,
            ContentHash: manifest.ContentHash,
            GeneratedAt: manifest.GeneratedAt);
    }

    private async Task PublishPackageAsync(string operation, CancellationToken cancellationToken)
    {
        var result = await _policyPackageService.PublishAsync(cancellationToken);
        if (result.Succeeded)
        {
            _logger.LogInformation(
                "Policy package publish completed for {Operation}. PackageId={PackageId} contentHash={ContentHash}",
                operation,
                result.PackageId,
                result.ContentHash);
            return;
        }

        _logger.LogWarning(
            "Policy package publish did not complete for {Operation}. PackageId={PackageId} contentHash={ContentHash} message={Message}",
            operation,
            result.PackageId,
            result.ContentHash,
            result.Message);
    }
}
