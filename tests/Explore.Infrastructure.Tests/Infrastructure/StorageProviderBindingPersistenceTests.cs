using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence.Configurations.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class StorageProviderBindingPersistenceTests
{
    [Test]
    public async Task TargetAndReferences_RoundTripWithoutOwnerRows_AndRejectMutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BindingContext>().UseSqlite(connection).Options;
        var retiredTenant = Guid.CreateVersion7();
        var access = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
            SecretScope.Tenant, retiredTenant, "ORIGINAL_STORAGE_ACCESS");
        access.Id = Guid.CreateVersion7();
        var secret = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey,
            SecretScope.Tenant, retiredTenant, "ORIGINAL_STORAGE_SECRET");
        secret.Id = Guid.CreateVersion7();
        var binding = StorageProviderBinding.S3("https://original.example.test", "original-bucket", "original-region", true,
            RetainedSecretReference.Capture(access, "Environment"), RetainedSecretReference.Capture(secret, "Environment"));
        await using (var write = new BindingContext(options))
        {
            await write.Database.EnsureCreatedAsync();
            write.Add(binding);
            await write.SaveChangesAsync();
        }
        // There are deliberately no tenant, resource or secret-binding rows in this database.
        await using var read = new BindingContext(options);
        var loaded = await read.Set<StorageProviderBinding>().SingleAsync();
        await Assert.That(loaded.Id).IsEqualTo(binding.Id);
        await Assert.That(loaded.BucketName).IsEqualTo("original-bucket");
        await Assert.That(loaded.AccessKeyReference!.BindingId).IsEqualTo(access.Id);
        await Assert.That(loaded.AccessKeyReference.ScopeId).IsEqualTo(retiredTenant);
        await Assert.That(loaded.AccessKeyReference.EnvironmentVariableName).IsEqualTo("ORIGINAL_STORAGE_ACCESS");
        read.Entry(loaded).Property(value => value.BucketName).CurrentValue = "replacement-bucket";
        await Assert.ThrowsAsync<InvalidOperationException>(() => read.SaveChangesAsync());
    }

    [Test]
    public async Task ExternalReference_IsImmutableAfterPersistence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BindingContext>().UseSqlite(connection).Options;
        await using var context = new BindingContext(options);
        await context.Database.EnsureCreatedAsync();
        var access = SecretBinding.CreateInfisical(SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
            SecretScope.Instance, null, "original-environment", "/original-path", "ORIGINAL_ACCESS");
        var secret = SecretBinding.CreateInfisical(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey,
            SecretScope.Instance, null, "original-environment", "/original-path", "ORIGINAL_SECRET");
        var binding = StorageProviderBinding.S3("https://original.example.test", "bucket", "region", true,
            RetainedSecretReference.Capture(access, "Infisical", "https://secrets.example.test", "original-project"),
            RetainedSecretReference.Capture(secret, "Infisical", "https://secrets.example.test", "original-project"));
        context.Add(binding);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var loaded = await context.Set<StorageProviderBinding>().SingleAsync();
        await Assert.That(loaded.SecretKeyReference!.AuthorityProject).IsEqualTo("original-project");
        context.Entry(loaded.SecretKeyReference).Property(value => value.InfisicalPath).CurrentValue = "/replacement-path";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    private sealed class BindingContext(DbContextOptions<BindingContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyConfiguration(new StorageProviderBindingConfiguration());
    }
}
