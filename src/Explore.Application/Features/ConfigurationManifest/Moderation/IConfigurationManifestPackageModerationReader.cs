namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// Reads one tenant's moderation definition for a configuration package on a fresh tenantless Global
/// context in its own serializable transaction, re-checking the caller's package authority around it.
/// </summary>
/// <remarks>
/// Runtime holds no grant on moderation sources. A package reader therefore calls this before and after
/// its Runtime settings snapshot and rejects the package unless both reads return the same
/// <see cref="ModerationConfigurationExport.PublicationIds"/>.
/// </remarks>
public interface IConfigurationManifestPackageModerationReader
{
    Task<ModerationConfigurationExport> ReadAsync(Guid tenantId, CancellationToken cancellationToken);
}
