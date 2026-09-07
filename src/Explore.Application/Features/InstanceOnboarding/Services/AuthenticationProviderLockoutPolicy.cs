using Explore.Application.DTOs.Onboarding;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public static class AuthenticationProviderLockoutPolicy
{
    public static bool PreservesCurrentAdministratorAccess(
        IReadOnlyCollection<UserExternalLogin> userExternalLogins,
        AuthProviderConfigurationDto configuration)
    {
        var enabledProviders = new HashSet<int>();
        if (configuration.PrimaryProviderId is
            (int)AuthenticationProviderKind.Local
            or (int)AuthenticationProviderKind.Keycloak
            or (int)AuthenticationProviderKind.Atproto)
        {
            enabledProviders.Add(configuration.PrimaryProviderId);
        }

        if (configuration.GoogleSsoEnabled)
        {
            enabledProviders.Add((int)AuthenticationProviderKind.Google);
        }

        if (configuration.AtprotoLoginEnabled)
        {
            enabledProviders.Add((int)AuthenticationProviderKind.Atproto);
        }

        if (userExternalLogins.Count == 0)
        {
            return false;
        }

        return userExternalLogins.Any(login =>
            enabledProviders.Contains(login.AuthenticationProviderId));
    }
}
