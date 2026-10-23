using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class ExternalDataProtectionHostTests
{
    [Test]
    public async Task ApiHostProtectsThroughTheAdmittedApiPartition()
    {
        await using var host = new ExternalApiPhase0WebApplicationFactory();
        using var client = host.CreateClient();
        var admitted = host.Services.GetRequiredService<ExternalDataProtectionProvider>();
        var provider = host.Services.GetRequiredService<IDataProtectionProvider>();

        await Assert.That(admitted.Admission.Partition).IsEqualTo("api");
        await Assert.That(ReferenceEquals(admitted, provider)).IsTrue();
        var protector = provider.CreateProtector("Event.ApiHost.ExternalAuthority.v1");
        byte[] value = RandomNumberGenerator.GetBytes(32);
        byte[] recovered = protector.Unprotect(protector.Protect(value));
        await Assert.That(CryptographicOperations.FixedTimeEquals(recovered, value)).IsTrue();
    }
}
