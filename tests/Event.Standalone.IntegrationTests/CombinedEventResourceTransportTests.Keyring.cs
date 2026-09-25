using System.Security.Cryptography;
using Event.Standalone.IntegrationTests.Fixtures;
using Event.Standalone.Hosting;
using Explore.Blazor.Extensions;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Standalone.IntegrationTests;

public sealed partial class CombinedEventResourceTransportTests
{
    [Test]
    public async Task OptionalBffRedisRegistrationCannotReplaceTheCombinedApiDestinationKeyring()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DataProtectionKeyContext>();
        services.AddDataProtection().SetApplicationName(BffDataProtectionExtensions.ApplicationName)
            .PersistKeysToDbContext<DataProtectionKeyContext>();
        services.AddBffDataProtection(Guid.CreateVersion7().ToString("N"));
        services.AddCombinedApiDataProtection();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        await Assert.That(options.XmlRepository?.GetType().FullName ?? string.Empty)
            .Contains("EntityFrameworkCore");
    }

    [Test]
    public async Task CombinedHostWithoutApiDatabaseKeepsItsBffKeyAuthority()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBffDataProtection(Guid.CreateVersion7().ToString("N"));
        services.AddCombinedApiDataProtection();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        await Assert.That(options.XmlRepository?.GetType().FullName ?? string.Empty)
            .DoesNotContain("EntityFrameworkCore");
    }

    [Test]
    public async Task CombinedApiRetainsTheActualDatabaseKeyringAcrossRestart()
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
            var options = scope.ServiceProvider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
            await Assert.That(options.XmlRepository?.GetType().FullName ?? string.Empty)
                .Contains("EntityFrameworkCore");
            var keys = scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>();
            await Assert.That(await keys.DataProtectionKeys.CountAsync()).IsGreaterThan(0);
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
