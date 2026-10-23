using System.Security.Cryptography;
using Event.Standalone.IntegrationTests.Fixtures;
using Event.Standalone.Hosting;
using Event.Testing.DataProtection;
using Explore.Blazor.Extensions;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Standalone.IntegrationTests;

public sealed partial class CombinedEventResourceTransportTests
{
    [Test]
    public async Task CombinedCompositionUsesOnlyItsExplicitExternalPartition()
    {
        var services = new ServiceCollection();
        services.AddExternalDataProtection("api");
        services.AddBffDataProtection();
        services.AddCombinedApiDataProtection();
        services.AddSingleton<IExternalDataProtectionSource>(new ExternalDataProtectionTestAuthority("combined"));
        using var provider = services.BuildServiceProvider();
        var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("destination");
        await Assert.That(protector.Unprotect(protector.Protect("synthetic"))).IsEqualTo("synthetic");
        await Assert.That(provider.GetRequiredService<ExternalDataProtectionProvider>().Admission.Partition)
            .IsEqualTo("combined");
        var keys = provider.GetRequiredService<IKeyManager>();
        await Assert.That(keys.GetAllKeys().Count).IsEqualTo(1);
        await Assert.That(() => keys.CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => keys.RevokeAllKeys(DateTimeOffset.UtcNow)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CombinedCannotBorrowBffAuthorityWhenItsOwnRingIsMissing()
    {
        var services = new ServiceCollection();
        services.AddBffDataProtection();
        services.AddCombinedApiDataProtection();
        services.AddSingleton<IExternalDataProtectionSource>(new ExternalDataProtectionTestAuthority("bff"));
        using var provider = services.BuildServiceProvider();
        await Assert.That(() => provider.GetRequiredService<IDataProtectionProvider>())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CombinedApiRetainsExternalDestinationAcrossActualHostRestart()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        Guid resourceId = Guid.CreateVersion7();
        string destination = $"https://files.example.org/material?key={Guid.CreateVersion7():N}";
        string protectedValue;
        await using (var first = deployment.CreateHost())
        {
            await using var scope = first.Services.CreateAsyncScope();
            var protector = scope.ServiceProvider.GetRequiredService<IEventResourceDestinationProtector>();
            protectedValue = protector.Protect(destination, PlatformDefaults.DefaultTenantId, resourceId,
                protector.CurrentVersion);
            await Assert.That(scope.ServiceProvider.GetRequiredService<ExternalDataProtectionProvider>().Admission.Partition)
                .IsEqualTo("combined");
        }
        await using (var restarted = deployment.CreateHost())
        {
            await using var scope = restarted.Services.CreateAsyncScope();
            var protector = scope.ServiceProvider.GetRequiredService<IEventResourceDestinationProtector>();
            await Assert.That(protector.Unprotect(protectedValue, PlatformDefaults.DefaultTenantId,
                resourceId, protector.CurrentVersion)).IsEqualTo(destination);
            await Assert.ThrowsAsync<CryptographicException>(() => Task.FromResult(
                protector.Unprotect(protectedValue, PlatformDefaults.DefaultTenantId,
                    Guid.CreateVersion7(), protector.CurrentVersion)));
        }
    }
}
