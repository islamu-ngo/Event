using System.Data;
using System.Text.Json.Nodes;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

public sealed partial class ConfigurationManifestAtomicPersistenceTests
{
    [Test]
    public async Task BootstrapRevision_CreatesCanonicalMetadataOnlyForNewTenantsAndNeverRepairsSkippedOnes()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            var options = RevisionOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            Guid retainedTenantId = Guid.CreateVersion7(), missingTenantId = Guid.CreateVersion7();
            await using (var seed = factory.CreateDbContext())
            {
                seed.ClearTenantFilterBypass();
                seed.BindTenantLease();
                seed.AddRange(RevisionTenant(retainedTenantId, "retained"), RevisionTenant(missingTenantId, "missing"),
                    new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = retainedTenantId, IdentityEpoch = 7, DisclosureEpoch = 19 },
                    new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = missingTenantId, IdentityEpoch = 0, DisclosureEpoch = 1 });
                await seed.SaveChangesAsync();
                await seed.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
                    .Where(row => row.TenantId == missingTenantId).ExecuteDeleteAsync();
            }

            await using var verification = factory.CreateDbContext();
            var retainedBefore = await verification.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(row => row.TenantId == retainedTenantId);
            var source = ConfigurationManifestApplicationTestSupport.Source("retained", "missing", "new-revision");
            await using (var host = ConfigurationManifestApplicationTestSupport.CreateHandler(factory))
                await Assert.That((await host.ExecuteAsync(new ApplyConfigurationManifestCommand(source), CancellationToken.None)).IsSuccess)
                    .IsTrue();

            Guid createdTenantId = await verification.Tenants.AsNoTracking().Where(row => row.Slug == "new-revision")
                .Select(row => row.Id).SingleAsync();
            var created = await verification.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(row => row.TenantId == createdTenantId);
            await Assert.That((created.Id.Version, created.TenantId, created.IdentityEpoch, created.DisclosureEpoch))
                .IsEqualTo((7, createdTenantId, 0L, 1L));

            await using (var host = ConfigurationManifestApplicationTestSupport.CreateHandler(factory))
                await Assert.That((await host.ExecuteAsync(new ApplyConfigurationManifestCommand(source), CancellationToken.None)).IsSuccess)
                    .IsTrue();

            var rows = await verification.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking().ToArrayAsync();
            await Assert.That(rows.Length).IsEqualTo(2);
            await Assert.That(rows.Any(row => row.TenantId == missingTenantId)).IsFalse();
            var retainedAfter = rows.Single(row => row.TenantId == retainedTenantId);
            await Assert.That((retainedAfter.Id, retainedAfter.TenantId, retainedAfter.IdentityEpoch, retainedAfter.DisclosureEpoch))
                .IsEqualTo((retainedBefore.Id, retainedTenantId, 7L, 19L));
            var createdAfter = rows.Single(row => row.TenantId == createdTenantId);
            await Assert.That((createdAfter.Id, createdAfter.TenantId, createdAfter.IdentityEpoch, createdAfter.DisclosureEpoch))
                .IsEqualTo((created.Id, createdTenantId, 0L, 1L));
            var operations = await verification.ConfigurationManifestOperations.AsNoTracking().ToArrayAsync();
            await Assert.That(operations.Select(row => (row.CreatedTenantCount, row.SkippedExistingTenantCount)))
                .IsEquivalentTo([(1, 2), (0, 3)]);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task BootstrapRevision_SecondRevisionInsertFailureRollsBackTheWholeOriginalUnit()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            var options = RevisionOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            await using var host = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);
            bool observedOriginal = false;
            host.AfterObservation = async (actual, plan, token) =>
            {
                observedOriginal = true;
                Guid secondTenant = plan.Tenants.Single(row => row.Slug == "second-revision").PlannedTenantId;
                var mapping = actual.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
                var store = StoreObjectIdentifier.Table(mapping.GetTableName()!, mapping.GetSchema());
                var sql = actual.GetService<ISqlGenerationHelper>();
                string table = sql.DelimitIdentifier(mapping.GetTableName()!, mapping.GetSchema());
                string tenant = sql.DelimitIdentifier(mapping.FindProperty(nameof(EventDiscoveryRevision.TenantId))!.GetColumnName(store)!);
                await actual.Database.ExecuteSqlRawAsync($"""
                    CREATE TRIGGER reject_second_bootstrap_revision BEFORE INSERT ON {table}
                    WHEN lower(NEW.{tenant}) = '{secondTenant:D}'
                    BEGIN SELECT RAISE(ABORT, 'actual second revision persistence rejected'); END;
                    """, token);
            };

            var response = await host.ExecuteAsync(new ApplyConfigurationManifestCommand(
                ConfigurationManifestApplicationTestSupport.Source("first-revision", "second-revision")), CancellationToken.None);

            await Assert.That(observedOriginal).IsTrue();
            await Assert.That(response.IsSuccess).IsFalse();
            await using var verification = factory.CreateDbContext();
            await Assert.That(await verification.Tenants.CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.Set<EventDiscoveryRevision>().IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.TenantSettingOverrides.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.TenantSettingsDocuments.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.OutboxMessages.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.ConfigurationManifestTenantResults.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
            var failed = await verification.ConfigurationManifestOperations.AsNoTracking().SingleAsync();
            await Assert.That(failed.Status).IsEqualTo(ConfigurationManifestOperationStatus.Failed);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    [Arguments("extra-member")]
    [Arguments("missing-before")]
    public async Task BootstrapRevision_PortableWriterRejectsMalformedEffectEnvelopesWithoutAnyEffect(string scenario)
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            var options = RevisionOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            Guid retainedTenantId = Guid.CreateVersion7();
            await using (var seed = factory.CreateDbContext())
            {
                seed.ClearTenantFilterBypass();
                seed.BindTenantLease();
                seed.AddRange(RevisionTenant(retainedTenantId, "retained-envelope"), new EventDiscoveryRevision
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = retainedTenantId,
                    IdentityEpoch = 7,
                    DisclosureEpoch = 19
                });
                await seed.SaveChangesAsync();
            }

            await using var original = factory.CreateDbContext();
            original.ClearTenantFilterBypass();
            original.BindTenantLease();
            await original.Database.OpenConnectionAsync();
            await using var custody = await new EventDiscoveryEnrollmentGate(original).AcquireAsync(CancellationToken.None);
            var data = new ConfigurationManifestBootstrapData(original);
            var writer = new ConfigurationManifestBootstrapProviderOperations(original, data, new ConfigurationBuilder().Build());
            Guid tenantId = Guid.CreateVersion7(), revisionId = Guid.CreateVersion7();
            var scope = new JsonObject
            {
                ["Slugs"] = new JsonArray(JsonValue.Create("retained-envelope"), JsonValue.Create("new-envelope")),
                ["Tenants"] = new JsonArray(JsonValue.Create(retainedTenantId), JsonValue.Create(tenantId)),
                ["CreatedTenants"] = new JsonArray(JsonValue.Create(tenantId)),
                ["Revisions"] = new JsonArray(JsonValue.Create(revisionId)),
                ["Documents"] = new JsonArray(),
                ["Policies"] = new JsonArray(),
                ["Operations"] = new JsonArray(),
                ["Operation"] = JsonValue.Create(Guid.CreateVersion7()),
                ["Effect"] = JsonValue.Create(Guid.CreateVersion7())
            };
            JsonObject before;
            JsonObject after;
            JsonArray changes;
            await using (var control = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                before = await writer.ReadAsync(scope, CancellationToken.None);
                after = before.DeepClone().AsObject();
                changes = new JsonArray();
                foreach (var (tag, row) in new[]
                {
                    ("Tenants", data.Row(RevisionTenant(tenantId, "new-envelope"))),
                    ("Revisions", data.Row(new EventDiscoveryRevision
                    {
                        Id = revisionId, TenantId = tenantId, IdentityEpoch = 0, DisclosureEpoch = 1
                    }))
                })
                {
                    changes.Add(new JsonObject { ["Tag"] = tag, ["Before"] = null, ["After"] = row.DeepClone() });
                    after[tag]!.AsArray().Add(row.DeepClone());
                }
                data.Order(after);
                await writer.WriteMutationAsync(scope, before, changes, after, CancellationToken.None);
                await Assert.That(ConfigurationManifestBootstrapData.Same(after,
                    await writer.ReadAsync(scope, CancellationToken.None))).IsTrue();
                await control.RollbackAsync();
            }

            var malformed = changes.DeepClone().AsArray();
            var revision = malformed.OfType<JsonObject>().Single(change => change["Tag"]!.GetValue<string>() == "Revisions");
            if (scenario == "extra-member") revision["Unexpected"] = true;
            else revision.Remove("Before");

            Exception? rejection = null;
            JsonObject observed;
            await using (var transaction = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await Assert.That(ConfigurationManifestBootstrapData.Same(before,
                    await writer.ReadAsync(scope, CancellationToken.None))).IsTrue();
                try
                {
                    await writer.WriteMutationAsync(scope, before, malformed, after, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    rejection = exception;
                }
                observed = await writer.ReadAsync(scope, CancellationToken.None);
                await transaction.RollbackAsync();
            }
            await Assert.That(rejection is UnauthorizedAccessException).IsTrue();
            await Assert.That(ConfigurationManifestBootstrapData.Same(before, observed)).IsTrue();
            await Assert.That(ConfigurationManifestBootstrapData.Same(before,
                await writer.ReadAsync(scope, CancellationToken.None))).IsTrue();
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static Tenant RevisionTenant(Guid id, string slug) => new()
    {
        Id = id,
        Slug = slug,
        FullName = slug,
        TenantStatusId = (int)TenantStatusEnum.Provisioning,
        TenantStatus = null!,
        CreatedAt = DateTime.UtcNow
    };

    private static DbContextOptions<ExploreDbContext> RevisionOptions(string databasePath) =>
        new DbContextOptionsBuilder<ExploreDbContext>(CreateOptions(databasePath))
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
}
