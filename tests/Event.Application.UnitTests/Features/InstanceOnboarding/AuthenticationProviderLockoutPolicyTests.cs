using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.InstanceOnboarding;

public sealed class AuthenticationProviderLockoutPolicyTests
{
    [Test]
    public async Task MissingProviderBindingsFailClosed()
    {
        bool result =
            AuthenticationProviderLockoutPolicy
                .PreservesCurrentAdministratorAccess(
                    [],
                    new AuthProviderConfigurationDto
                    {
                        PrimaryProviderId =
                            (int)AuthenticationProviderKind.Local
                    });

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AtprotoPrimaryPreservesExactAtprotoBinding()
    {
        bool result =
            AuthenticationProviderLockoutPolicy
                .PreservesCurrentAdministratorAccess(
                    [Login(AuthenticationProviderKind.Atproto)],
                    new AuthProviderConfigurationDto
                    {
                        PrimaryProviderId =
                            (int)AuthenticationProviderKind.Atproto,
                        AtprotoLoginEnabled = true
                    });

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task UnrelatedBindingCannotAuthorizeProviderSwitch()
    {
        bool result =
            AuthenticationProviderLockoutPolicy
                .PreservesCurrentAdministratorAccess(
                    [Login(AuthenticationProviderKind.Keycloak)],
                    new AuthProviderConfigurationDto
                    {
                        PrimaryProviderId =
                            (int)AuthenticationProviderKind.Local
                    });

        await Assert.That(result).IsFalse();
    }

    private static UserExternalLogin Login(
        AuthenticationProviderKind provider) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            User = null!,
            AuthenticationProviderId = (int)provider,
            AuthenticationProvider = null!,
            ProviderKey = $"provider-{Guid.CreateVersion7():N}"
        };
}
