using Explore.Domain.Secrets;

namespace Explore.Application.Contracts.Secrets;

/// <summary>
/// Retains the existing winning external reference for long-lived storage cleanup.
/// Resolution never consults current binding rows or falls back to another source.
/// </summary>
public interface IRetainedSecretResolver
{
    Task<RetainedSecretReference> CaptureAsync(string settingKey, Guid tenantId, CancellationToken cancellationToken);
    Task<SecretResolutionResult> ResolveAsync(RetainedSecretReference reference, CancellationToken cancellationToken);
}
