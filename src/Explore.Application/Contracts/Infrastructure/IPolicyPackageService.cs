using Explore.Application.Authorization;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Builds provider-neutral policy package manifests and publishes the current package through Infrastructure.
/// </summary>
public interface IPolicyPackageService
{
    /// <summary>
    /// Builds a provider-neutral manifest for the current authorization policy package without publishing it.
    /// </summary>
    Task<PolicyPackageManifest> BuildManifestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the current authorization policy package to the configured provider.
    /// </summary>
    Task<PolicyPackagePublishResult> PublishAsync(
        CancellationToken cancellationToken = default,
        PolicyPackageAdminCredentials? oneTimeCredentials = null);

    /// <summary>
    /// Publishes the current authorization policy package to the instance-managed provider target.
    /// </summary>
    Task<PolicyPackagePublishResult> PublishInstanceAsync(
        CancellationToken cancellationToken = default,
        PolicyPackageAdminCredentials? oneTimeCredentials = null);

    /// <summary>
    /// Gets an operator-safe status summary for the current authorization policy package target.
    /// </summary>
    Task<PolicyPackageStatusResult> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the current authorization policy package as a downloadable archive for manual installation.
    /// </summary>
    Task<PolicyPackageArchive> ExportArchiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Request-scoped Cerbos Admin API credentials used only by the current publish operation.
/// </summary>
public sealed class PolicyPackageAdminCredentials
{
    public PolicyPackageAdminCredentials(string username, string password)
    {
        Username = username;
        Password = password;
    }

    public string Username { get; }

    public string Password { get; }

    public override string ToString() => nameof(PolicyPackageAdminCredentials);
}
