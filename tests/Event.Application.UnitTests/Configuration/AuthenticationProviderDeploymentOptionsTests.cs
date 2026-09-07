using Explore.Application.Configuration;

namespace Event.Application.UnitTests.Configuration;

public sealed class AuthenticationProviderDeploymentOptionsTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("local")]
    [Arguments("LOCAL")]
    [Arguments("keycloak")]
    [Arguments("atproto")]
    [Arguments("ATPROTO")]
    public async Task PrimaryProviderValidationAcceptsOnlySupportedBoundaryCodes(string? provider)
    {
        var options = new AuthenticationProviderDeploymentOptions
        {
            Provider = provider,
        };

        await Assert.That(AuthenticationProviderDeploymentOptions.IsValid(options)).IsTrue();
    }

    [Test]
    [Arguments("both")]
    [Arguments("unknown")]
    public async Task PrimaryProviderValidationRejectsNonPrimaryCodes(string provider)
    {
        var options = new AuthenticationProviderDeploymentOptions
        {
            Provider = provider,
        };

        await Assert.That(AuthenticationProviderDeploymentOptions.IsValid(options)).IsFalse();
        await Assert.That(() => options.GetProvider()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AtprotoPrimaryRejectsAnExplicitlyDisabledAtprotoLoginAxis()
    {
        var options = new AuthenticationProviderDeploymentOptions
        {
            Provider = "atproto",
            AtprotoLoginEnabled = false,
        };

        await Assert.That(AuthenticationProviderDeploymentOptions.IsValid(options)).IsFalse();
        await Assert.That(() => options.GetProvider())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LocalIdentitySecurityBoundsRejectUnsafeValues()
    {
        await Assert.That(LocalIdentityOptions.IsValid(new LocalIdentityOptions())).IsTrue();
        await Assert.That(LocalIdentityOptions.IsValid(new LocalIdentityOptions
        {
            LockoutThreshold = 0,
        })).IsFalse();
        await Assert.That(LocalIdentityOptions.IsValid(new LocalIdentityOptions
        {
            LockoutDurationMinutes = 0,
        })).IsFalse();
        await Assert.That(LocalIdentityOptions.IsValid(new LocalIdentityOptions
        {
            AccessTokenLifetimeMinutes = 61,
        })).IsFalse();
    }
}
