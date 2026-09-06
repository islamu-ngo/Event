// ABOUTME: Regression tests for persisted ASP.NET Core Data Protection key rings.
// ABOUTME: Verifies BFF session payloads survive a fresh provider using the shared key store.

using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.DataProtection;

public sealed class DataProtectionKeyPersistenceTests
{
    private const string ApplicationName = "islamu-event";
    private const string Purpose = "bff-session-regression";

    [Test]
    public async Task PersistedKeyRingAllowsFreshProviderToUnprotectExistingPayload()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        const string databaseName = "data-protection-key-ring-regression";
        const string payload = "authenticated-session-ticket";

        string protectedPayload;

        await using (var firstProvider = BuildServiceProvider(databaseName, databaseRoot))
        {
            var protector = firstProvider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(Purpose);

            protectedPayload = protector.Protect(payload);

            await using var scope = firstProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>();
            await Assert.That(await dbContext.DataProtectionKeys.CountAsync()).IsGreaterThanOrEqualTo(1);
        }

        await using (var secondProvider = BuildServiceProvider(databaseName, databaseRoot))
        {
            var protector = secondProvider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(Purpose);

            var unprotectedPayload = protector.Unprotect(protectedPayload);

            await Assert.That(unprotectedPayload).IsEqualTo(payload);
        }
    }

    [Test]
    public async Task RelationalBackupRestoreRetainsKeyAndUnprotectsPreRestorePayload()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"payment-dp-restore-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "keys.db");
        string backupPath = Path.Combine(directory, "keys.backup.db");
        const string payload = "pre-restore-payment-capability";
        try
        {
            string protectedPayload;
            int keyCount;
            await using (ServiceProvider first = BuildRelationalServiceProvider(databasePath))
            {
                protectedPayload = first.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect(payload);
                await using AsyncServiceScope scope = first.CreateAsyncScope();
                DataProtectionKeyContext context = scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>();
                keyCount = await context.DataProtectionKeys.CountAsync();
                await Assert.That(keyCount).IsGreaterThanOrEqualTo(1);
                await Assert.That(await context.DataProtectionKeys.AllAsync(key => key.Xml.Contains("activationDate") && key.Xml.Contains("expirationDate"))).IsTrue();
            }

            File.Copy(databasePath, backupPath, overwrite: true);
            File.Delete(databasePath);
            File.Copy(backupPath, databasePath);

            await using ServiceProvider restored = BuildRelationalServiceProvider(databasePath);
            string unprotected = restored.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Unprotect(protectedPayload);
            await using AsyncServiceScope restoredScope = restored.CreateAsyncScope();
            DataProtectionKeyContext restoredContext = restoredScope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>();
            await Assert.That(unprotected).IsEqualTo(payload);
            await Assert.That(await restoredContext.DataProtectionKeys.CountAsync()).IsEqualTo(keyCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceProvider BuildRelationalServiceProvider(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddDbContext<DataProtectionKeyContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddDataProtection().SetApplicationName(ApplicationName).PersistKeysToDbContext<DataProtectionKeyContext>();
        ServiceProvider provider = services.BuildIsolatedServiceProvider(validateScopes: true);
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>().Database.EnsureCreated();
        return provider;
    }

    private static ServiceProvider BuildServiceProvider(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var services = new ServiceCollection();

        services.AddDbContext<DataProtectionKeyContext>(options =>
            options.UseTestInMemoryDatabase(databaseName, databaseRoot));

        services
            .AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<DataProtectionKeyContext>();

        var serviceProvider = services.BuildIsolatedServiceProvider(validateScopes: true);

        using var scope = serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>().Database.EnsureCreated();

        return serviceProvider;
    }
}
