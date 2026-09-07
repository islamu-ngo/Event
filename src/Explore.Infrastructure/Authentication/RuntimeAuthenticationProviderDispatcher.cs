using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Authentication;

internal sealed class RuntimeAuthenticationProviderDispatcher(
    ISystemSettingRepository systemSettingRepository,
    IMemoryCache cache,
    IOptions<AuthenticationProviderDeploymentOptions> deploymentOptions)
    : IAuthenticationProviderDispatcher,
      IAuthenticationProviderModeCacheInvalidator
{
    private const string InstanceModeCacheKey = "AuthenticationProvider_Mode";

    private static readonly TimeSpan InstanceModeCacheDuration =
        TimeSpan.FromMinutes(1);

    public async Task<AuthenticationProviderKind> GetActivePrimaryProviderAsync(
        CancellationToken cancellationToken)
    {
        string? deploymentProvider = deploymentOptions.Value.GetProvider();
        if (deploymentProvider is not null)
        {
            return ParseBoundaryProvider(deploymentProvider);
        }

        return await cache.GetOrCreateAsync(
            InstanceModeCacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = InstanceModeCacheDuration;
                try
                {
                    var setting = await systemSettingRepository.GetByKey(
                        GovernanceSettingKeys.Authentication.PrimaryProviderId,
                        cancellationToken).ConfigureAwait(false);
                    return ParsePersistedProvider(setting?.Value);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "The primary authentication provider could not be resolved.",
                        exception);
                }
            }).ConfigureAwait(false);
    }

    public void InvalidateInstanceMode() => cache.Remove(InstanceModeCacheKey);

    private static AuthenticationProviderKind ParseBoundaryProvider(string provider) =>
        provider switch
        {
            AuthenticationProviderDeploymentOptions.LocalProvider =>
                AuthenticationProviderKind.Local,
            AuthenticationProviderDeploymentOptions.KeycloakProvider =>
                AuthenticationProviderKind.Keycloak,
            AuthenticationProviderDeploymentOptions.AtprotoProvider =>
                AuthenticationProviderKind.Atproto,
            _ => throw new InvalidOperationException(
                "The deployment primary authentication provider is invalid.")
        };

    private static AuthenticationProviderKind ParsePersistedProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AuthenticationProviderKind.Local;
        }

        int providerId;
        try
        {
            providerId = JsonSerializer.Deserialize<int>(value);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The persisted primary authentication provider is invalid.",
                exception);
        }

        AuthenticationProviderKind provider = (AuthenticationProviderKind)providerId;
        return provider is AuthenticationProviderKind.Local
            or AuthenticationProviderKind.Keycloak
            or AuthenticationProviderKind.Atproto
            ? provider
            : throw new InvalidOperationException(
                "The persisted primary authentication provider is not a supported primary authority.");
    }
}
