namespace Explore.Application.Contracts.Operations;

/// <summary>
/// Confirms that the original human caller may disclose one tenant's configuration package.
/// </summary>
/// <remarks>
/// Every call is a fresh, independent read: callers check before reading tenant configuration and
/// again after buffering it, so revocation or credential expiry during the read is observed before
/// any bytes are returned. Failure throws <see cref="UnauthorizedAccessException"/>.
/// </remarks>
public interface IConfigurationManifestPackageAuthority
{
    Task RequireAsync(Guid tenantId, CancellationToken cancellationToken);
}
