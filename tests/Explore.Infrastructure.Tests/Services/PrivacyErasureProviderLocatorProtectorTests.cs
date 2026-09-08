using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Services;

public sealed class PrivacyErasureProviderLocatorProtectorTests
{
    [Test]
    public async Task Protect_RoundTripsWithoutPersistingPlaintext()
    {
        var protector = new PrivacyErasureProviderLocatorProtector(new EphemeralDataProtectionProvider());

        string protectedLocator = protector.Protect("provider-account-123", TimeSpan.FromMinutes(5));

        await Assert.That(protectedLocator).IsNotEqualTo("provider-account-123");
        await Assert.That(protector.Unprotect(protectedLocator)).IsEqualTo("provider-account-123");
        await Assert.That(() => protector.Protect(" ", TimeSpan.FromMinutes(5))).Throws<ArgumentException>();
        await Assert.That(() => protector.Protect("provider-account-123", TimeSpan.Zero))
            .Throws<ArgumentOutOfRangeException>();
    }
}
