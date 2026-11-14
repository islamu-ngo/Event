using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EfFirstReadContractTests
{
    [Test]
    public async Task DomainDiscoveryMatchesExactNormalizedHostsAcrossTenantsAndExcludesPrivateSettings()
    {
        await using var connection = new SqliteConnection(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString());
        await connection.OpenAsync();
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options);
        context.EnableTenantFilterBypass("Domain discovery contract arrangement.");
        context.BindTenantLease();
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        var tenant = new Tenant { Id = Guid.CreateVersion7(), FullName = "Domain owner",
            Slug = $"domain-{Guid.CreateVersion7():N}", TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null! };
        context.Add(tenant);
        context.AddRange(
            new TenantSetting { Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                SettingKey = "domains.tenant_custom_domain", Value = SettingValueSerializer.Serialize("BOUND.EXAMPLE.TEST.") },
            new TenantSetting { Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                SettingKey = "domains.tenant_subdomain", Value = SettingValueSerializer.Serialize("sub.example.test") },
            new TenantSetting { Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                SettingKey = "email.smtp_host", Value = SettingValueSerializer.Serialize("private.example.test") });
        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();
        context.ChangeTracker.Clear();

        async Task<TenantSetting?> Find(string host) => await ManagedTenantDomainQuery.ReadAsync(context,
            SettingValueSerializer.Serialize(host), SettingValueSerializer.Serialize(host + "."), CancellationToken.None);

        var custom = await Find("bound.example.test");
        await Assert.That(custom?.TenantId).IsEqualTo(tenant.Id);
        await Assert.That(custom?.SettingKey).IsEqualTo("domains.tenant_custom_domain");
        await Assert.That((await Find("sub.example.test"))?.SettingKey).IsEqualTo("domains.tenant_subdomain");
        await Assert.That(await Find("private.example.test")).IsNull();
        await Assert.That(await Find("example.test")).IsNull();
        await Assert.That(await Find("unbound.example.test")).IsNull();
        await Assert.That(context.TenantFilterTenantId).IsNull();
    }

    [Test]
    public async Task StorageTotalsRetainCustodyAndExcludeOtherTenantsAndTombstones()
    {
        await using var connection = new SqliteConnection(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString());
        await connection.OpenAsync();
        var commands = new ReadCommandCounter();
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, commands).Options);
        context.EnableTenantFilterBypass("Storage accounting read contract arrangement.");
        context.BindTenantLease();
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        var tenant = new Tenant { FullName = "Accounting", Slug = $"accounting-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null! };
        var otherTenant = new Tenant { FullName = "Other", Slug = $"other-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null! };
        context.AddRange(tenant, otherTenant);
        var binding = StorageProviderBinding.Local(Path.Combine(Path.GetTempPath(), "accounting-fixture"));
        var user = new User { Id = Guid.CreateVersion7(), Pii = new UserPii
            { Email = "accounting@example.test", FirstName = "Accounting", LastName = "Fixture" } };
        context.AddRange(binding, user);
        await context.SaveChangesAsync();
        var empty = await StorageScopeTotalsQuery.ReadAsync(context, tenant.Id, StorageProviders.Local, CancellationToken.None);
        await Assert.That((empty.Used, empty.Reserved, empty.Quarantined, empty.Count)).IsEqualTo((0L, 0L, 0L, 0L));

        int fileTypeId = await context.FileTypes.Select(value => value.Id).FirstAsync();
        StorageObject Object(Tenant owner, long bytes, string lifecycle, bool deleted = false) => new()
        {
            Id = Guid.CreateVersion7(), TenantId = owner.Id, Tenant = owner, FileTypeId = fileTypeId, FileType = null!,
            StorageProviderBindingId = binding.Id, ObjectKey = Guid.CreateVersion7().ToString("N"),
            Provider = StorageProviders.Local, FullName = "fixture", SafeDisplayName = "fixture", Extension = ".bin",
            Size = bytes, Purpose = StorageObjectPurposes.Attachment, Visibility = StorageObjectVisibilities.PrivateOwner,
            LifecycleState = lifecycle, IsDeleted = deleted, ConcurrencyStamp = Guid.CreateVersion7()
        };
        var active = Object(tenant, 17, StorageObjectLifecycleStates.Active);
        var held = Object(tenant, 23, StorageObjectLifecycleStates.Quarantined);
        var softDeleted = Object(tenant, 31, StorageObjectLifecycleStates.Active, deleted: true);
        var tombstoned = Object(tenant, 47, StorageObjectLifecycleStates.Active);
        var finalizedResource = Object(tenant, 59, StorageObjectLifecycleStates.Pending);
        var unfinishedResource = Object(tenant, 61, StorageObjectLifecycleStates.Pending);
        foreach (var resource in new[] { finalizedResource, unfinishedResource })
        {
            resource.Purpose = StorageObjectPurposes.EventResource;
            resource.OwningResourceKind = StorageOwningResourceKinds.EventResource;
            resource.OwningResourceId = Guid.CreateVersion7();
        }
        context.AddRange(active, held, softDeleted, tombstoned, finalizedResource, unfinishedResource,
            Object(tenant, 3_000_000_000, StorageObjectLifecycleStates.Active),
            Object(otherTenant, 97, StorageObjectLifecycleStates.Active));
        await context.SaveChangesAsync();

        StorageUploadSession Session(Tenant owner, long bytes, string? objectKey = null) => new()
        {
            Id = Guid.CreateVersion7(), TenantId = owner.Id, StorageProviderBindingId = binding.Id,
            Provider = StorageProviders.Local, ExpectedSizeBytes = bytes, ReservedBytes = bytes,
            ContentType = "application/octet-stream", SafeDisplayName = "fixture",
            Purpose = StorageObjectPurposes.Attachment, Visibility = StorageObjectVisibilities.PrivateOwner,
            Status = StorageUploadSessionStates.Reserved, ObjectKey = objectKey ?? Guid.CreateVersion7().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddHours(1), ConcurrencyStamp = Guid.CreateVersion7()
        };
        var reserved = Session(tenant, 71);
        var uploading = Session(tenant, 73);
        uploading.MarkUploading(DateTime.UtcNow);
        var expired = Session(tenant, 79);
        expired.MarkExpired(DateTime.UtcNow);
        var canceled = Session(tenant, 83);
        canceled.Cancel(DateTime.UtcNow);
        var failed = Session(tenant, 89);
        failed.Fail("fixture_failure", null, DateTime.UtcNow);
        var finalized = Session(tenant, 59, finalizedResource.ObjectKey);
        finalized.Purpose = StorageObjectPurposes.EventResource;
        finalized.UserId = user.Id;
        finalized.OwningResourceKind = StorageOwningResourceKinds.EventResource;
        finalized.OwningResourceId = finalizedResource.OwningResourceId;
        finalized.BindEventResourceVersion(Guid.CreateVersion7());
        finalized.MarkUploading(DateTime.UtcNow);
        finalized.StageEventResourceObject(finalizedResource.Id);
        finalized.RecordProducerSettlement(finalizedResource.Id, binding.Id, finalized.ObjectKey!, null);
        finalized.Finalize(finalizedResource.Id, finalized.ObjectKey!, null, DateTime.UtcNow);
        finalized.RecordFinalizedResourceVersion(Guid.CreateVersion7());
        context.AddRange(reserved, uploading, expired, canceled, failed, finalized, Session(otherTenant, 101));
        context.Add(StorageObjectDeletionTombstone.Create(tombstoned.Id, tenant.Id, StorageProviders.Local,
            binding.Id, tombstoned.ObjectKey!, null, true, DateTime.UtcNow));
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        commands.Count = 0;
        var totals = await StorageScopeTotalsQuery.ReadAsync(context, tenant.Id, StorageProviders.Local, CancellationToken.None);
        await Assert.That((totals.Used, totals.Reserved, totals.Quarantined, totals.Count))
            .IsEqualTo((3_000_000_107L, 144L, 23L, 5L));
        await Assert.That(commands.Count).IsEqualTo(1);
        var otherProvider = await StorageScopeTotalsQuery.ReadAsync(context, tenant.Id, StorageProviders.S3Compatible, CancellationToken.None);
        await Assert.That((otherProvider.Used, otherProvider.Reserved, otherProvider.Quarantined, otherProvider.Count))
            .IsEqualTo((0L, 0L, 0L, 0L));
    }

    private sealed class ReadCommandCounter : DbCommandInterceptor
    {
        internal int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
