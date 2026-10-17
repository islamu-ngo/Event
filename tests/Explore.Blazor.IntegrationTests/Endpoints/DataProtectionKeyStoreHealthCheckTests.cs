using System.Security.Cryptography;
using Event.Testing.DataProtection;
using Explore.Blazor.HealthChecks;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class DataProtectionKeyStoreHealthCheckTests
{
    [Test]
    public async Task AdmittedAuthorityReportsOnlySafeMetadata()
    {
        var authority = new ExternalDataProtectionTestAuthority("bff");
        using var provider = ExternalDataProtectionProvider.Create(authority.Input, "bff", TimeProvider.System);
        var check = new DataProtectionKeyStoreHealthCheck(new(authority, provider, TimeProvider.System));
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["generation"]).IsEqualTo(1L);
        await Assert.That(result.Data.Keys).DoesNotContain("xml");
        await Assert.That(result.Exception).IsNull();
    }

    [Test]
    public async Task ChangedAuthorityLatchesEveryCachedProtectorClosedUntilRestart()
    {
        var authority = new ExternalDataProtectionTestAuthority("bff");
        using var first = ExternalDataProtectionProvider.Create(authority.Input, "bff", TimeProvider.System);
        using var second = ExternalDataProtectionProvider.Create(authority.Input, "bff", TimeProvider.System);
        var cached = first.CreateProtector("cookie");
        byte[] protectedData = cached.Protect([1, 2, 3]);
        authority.Input = authority.Input with { MinimumGeneration = 2 };
        var result = await new DataProtectionKeyStoreHealthCheck(new(authority, first, TimeProvider.System))
            .CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(() => cached.Unprotect(protectedData)).Throws<CryptographicException>();
        // A different process is not magically refreshed by checking the first process.
        await Assert.That(second.CreateProtector("cookie").Unprotect(protectedData).Length).IsEqualTo(3);
        var secondResult = await new DataProtectionKeyStoreHealthCheck(new(authority, second, TimeProvider.System))
            .CheckHealthAsync(new HealthCheckContext());
        await Assert.That(secondResult.Status).IsEqualTo(HealthStatus.Unhealthy);
        authority.Input = authority.Input with { MinimumGeneration = 1 };
        await Assert.That(() => cached.Unprotect(protectedData)).Throws<CryptographicException>();
    }
}
