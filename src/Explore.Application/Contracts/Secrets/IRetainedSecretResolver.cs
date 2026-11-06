using Explore.Domain.Secrets;

namespace Explore.Application.Contracts.Secrets;

/// <summary>
/// Retains the existing winning external reference for long-lived storage cleanup.
/// Resolution never consults current binding rows or falls back to another source.
/// </summary>
public interface IRetainedSecretResolver
{
    Task<RetainedSecretReference> CaptureAsync(string settingKey, Guid tenantId, CancellationToken cancellationToken);
    /// <summary>
    /// Captures only the selected instance reference without reading external material.
    /// Returns null when no instance reference exists. Resolve after releasing source
    /// locks to establish availability, including an unconfigured external value.
    /// </summary>
    Task<RetainedSecretReference?> CaptureInstanceAsync(string settingKey, CancellationToken cancellationToken);
    Task<SecretResolutionResult> ResolveAsync(RetainedSecretReference reference, CancellationToken cancellationToken);
}
