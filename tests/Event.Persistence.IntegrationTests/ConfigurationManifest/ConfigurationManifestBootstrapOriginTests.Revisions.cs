using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

public sealed partial class ConfigurationManifestBootstrapOriginTests
{
    [Test]
    public async Task BootstrapRevision_OriginalGlobalCreatesCanonicalMetadataBehindItsPreBeginGate()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        int observedLocks = 0;
        host.BeforeLock = async (_, actual, _) =>
        {
            observedLocks++;
            EventDiscoveryEnrollmentGate.RequireCurrent(actual);
            actual.RequireBoundTenantLease();
            await Assert.That(actual.Database.CurrentTransaction).IsNull();
            await Assert.That(actual.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        };
        var response = await host.ExecuteAsync(new ApplyConfigurationManifestCommand(
            ConfigurationManifestApplicationTestSupport.Source("native-revision")), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(observedLocks).IsGreaterThan(0);
        await using var verification = fixture.CreateDbContext();
        var tenant = await verification.Tenants.AsNoTracking().SingleAsync(row => row.Slug == "native-revision");
        var revision = await verification.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.TenantId == tenant.Id);
        await Assert.That((revision.Id.Version, revision.TenantId, revision.IdentityEpoch, revision.DisclosureEpoch))
            .IsEqualTo((7, tenant.Id, 0L, 1L));
        var operation = await verification.ConfigurationManifestOperations.AsNoTracking().SingleAsync();
        await Assert.That((operation.Id, operation.Status, operation.CreatedTenantCount))
            .IsEqualTo((response.Id, ConfigurationManifestOperationStatus.Applied, 1));
    }

    [Test]
    public async Task BootstrapRevision_ValidateOnlyNeverAcquiresCreationCustodyOrWritesMetadata()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture, mode: ConfigurationManifestMode.ValidateOnly);
        bool observed = false;
        host.AfterObservation = async (actual, _, _) =>
        {
            observed = true;
            await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(actual)).IsFalse();
        };
        var response = await host.ExecuteAsync(new ApplyConfigurationManifestCommand(
            ConfigurationManifestApplicationTestSupport.Source("validate-revision")), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(observed).IsTrue();
        await AssertNoBootstrapAsync();
        await using var verification = fixture.CreateDbContext();
        await Assert.That(await verification.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task BootstrapRevision_RetainsBothCollisionImagesAndRollsBackAReservedIdentityConflict()
    {
        await fixture.ResetAsync();
        Guid selectedTenant = Guid.CreateVersion7(), otherTenant = Guid.CreateVersion7();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.ClearTenantFilterBypass();
            seed.BindTenantLease();
            seed.AddRange(NativeRevisionTenant(selectedTenant, "selected-revision"),
                NativeRevisionTenant(otherTenant, "reserved-revision"),
                new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = selectedTenant, IdentityEpoch = 0, DisclosureEpoch = 1 },
                new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = otherTenant, IdentityEpoch = 0, DisclosureEpoch = 1 });
            await seed.SaveChangesAsync();
        }
        await using var verification = fixture.CreateDbContext();
        var originals = await verification.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking()
            .OrderBy(row => row.Id).ToArrayAsync();
        await Assert.That(originals.Length).IsEqualTo(2);
        Guid reservedId = originals.Single(row => row.TenantId == otherTenant).Id;
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        await using var original = await OpenRevisionGlobalAsync(host);
        await using var transaction = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var scope = RevisionScope(selectedTenant, reservedId, "selected-revision");
        Guid newTenantId = Guid.CreateVersion7();
        scope["Slugs"]!.AsArray().Add(JsonValue.Create("collision-revision"));
        scope["Tenants"]!.AsArray().Add(JsonValue.Create(newTenantId));
        scope["CreatedTenants"] = new JsonArray(JsonValue.Create(newTenantId));
        var image = await ReadRevisionImageAsync(original, scope);

        var observed = image["Revisions"]!.AsArray().OfType<JsonObject>()
            .Select(row => (row[nameof(EventDiscoveryRevision.Id)]!.GetValue<Guid>(),
                row[nameof(EventDiscoveryRevision.TenantId)]!.GetValue<Guid>(),
                row[nameof(EventDiscoveryRevision.IdentityEpoch)]!.GetValue<long>(),
                row[nameof(EventDiscoveryRevision.DisclosureEpoch)]!.GetValue<long>())).ToArray();
        await Assert.That(observed).IsEquivalentTo(originals.Select(row => (row.Id, row.TenantId, 0L, 1L)));

        var data = new ConfigurationManifestBootstrapData(original);
        var after = image.DeepClone().AsObject();
        var changes = new JsonArray();
        AddRevisionTestEffect(after, changes, "Tenants", null,
            data.Row(NativeRevisionTenant(newTenantId, "collision-revision")));
        AddRevisionTestEffect(after, changes, "Revisions", null,
            JsonSerializer.SerializeToNode(new EventDiscoveryRevision
            {
                Id = reservedId,
                TenantId = newTenantId,
                IdentityEpoch = 0,
                DisclosureEpoch = 1
            })!.AsObject());
        data.Order(after);
        var rejection = await Assert.ThrowsAsync<PostgresException>(() => PostgresConfigurationManifestBootstrapContract.WriteAsync(
            original, data, scope, fixture.NativeQueueEnrollment.MigratorLogin,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global).Username!, image, changes, after, CancellationToken.None));
        // Existing native INSERT ON CONFLICT DO NOTHING must fail its exact affected-row proof.
        await Assert.That((rejection!.SqlState, rejection.MessageText))
            .IsEqualTo((PostgresErrorCodes.SerializationFailure, "manifest_bootstrap_preimage_changed"));
        await transaction.RollbackAsync();

        await Assert.That(await verification.Tenants.CountAsync()).IsEqualTo(2);
        await Assert.That(await verification.Tenants.AnyAsync(row => row.Id == newTenantId)).IsFalse();
        var retained = await verification.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking().ToArrayAsync();
        await Assert.That(retained.Select(row => (row.Id, row.TenantId, row.IdentityEpoch, row.DisclosureEpoch)))
            .IsEquivalentTo(originals.Select(row => (row.Id, row.TenantId, row.IdentityEpoch, row.DisclosureEpoch)));
        await Assert.That(await verification.ConfigurationManifestOperations.AnyAsync()).IsFalse();
        await Assert.That(await verification.ConfigurationManifestTenantResults.IgnoreQueryFilters().AnyAsync()).IsFalse();
        await Assert.That(await verification.OutboxMessages.IgnoreQueryFilters().AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("unpaired")]
    [Arguments("wrong-tenant")]
    [Arguments("noncanonical")]
    [Arguments("disclosure-epoch")]
    [Arguments("duplicate")]
    [Arguments("update")]
    public async Task BootstrapRevision_NativeWriterRejectsUnpairedOrNoncanonicalChangesWithoutAnyEffect(string scenario)
    {
        await fixture.ResetAsync();
        Guid existingTenant = Guid.CreateVersion7();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.ClearTenantFilterBypass();
            seed.BindTenantLease();
            seed.AddRange(NativeRevisionTenant(existingTenant, "existing-revision"),
                new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = existingTenant, IdentityEpoch = 0, DisclosureEpoch = 1 });
            await seed.SaveChangesAsync();
            if (scenario is "unpaired" or "wrong-tenant")
                await seed.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
                    .Where(row => row.TenantId == existingTenant).ExecuteDeleteAsync();
        }
        Guid tenantId = scenario is "unpaired" or "update" ? existingTenant : Guid.CreateVersion7();
        Guid revisionId = Guid.CreateVersion7();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        await using var original = await OpenRevisionGlobalAsync(host);
        await using var transaction = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var scope = RevisionScope(tenantId, revisionId,
            scenario is "unpaired" or "update" ? "existing-revision" : "uncommitted-revision");
        var data = new ConfigurationManifestBootstrapData(original);
        var before = await ReadRevisionImageAsync(original, scope);
        var after = before.DeepClone().AsObject();
        var changes = new JsonArray();
        if (scenario is not ("unpaired" or "update"))
            AddRevisionTestEffect(after, changes, "Tenants", null,
                data.Row(NativeRevisionTenant(tenantId, "uncommitted-revision")));
        if (scenario != "missing")
        {
            JsonObject? prior = scenario == "update" ? before["Revisions"]!.AsArray().OfType<JsonObject>().Single() : null;
            var revision = prior?.DeepClone().AsObject() ?? JsonSerializer.SerializeToNode(new EventDiscoveryRevision
            {
                Id = revisionId,
                TenantId = scenario == "wrong-tenant" ? existingTenant : tenantId,
                IdentityEpoch = 0,
                DisclosureEpoch = 1
            })!.AsObject();
            if (scenario is "noncanonical" or "update")
                revision[nameof(EventDiscoveryRevision.IdentityEpoch)] = 1L;
            if (scenario == "disclosure-epoch")
                revision[nameof(EventDiscoveryRevision.DisclosureEpoch)] = 0L;
            AddRevisionTestEffect(after, changes, "Revisions", prior, revision);
            if (scenario == "duplicate")
                changes.Add(changes.Last()!.DeepClone());
        }
        data.Order(after);

        var rejection = await Assert.ThrowsAsync<PostgresException>(() => PostgresConfigurationManifestBootstrapContract.WriteAsync(
            original, data, scope, fixture.NativeQueueEnrollment.MigratorLogin,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global).Username!, before, changes, after, CancellationToken.None));

        await Assert.That(rejection!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await transaction.RollbackAsync();
        await using var verification = fixture.CreateDbContext();
        await Assert.That(await verification.Tenants.CountAsync()).IsEqualTo(1);
        await Assert.That(await verification.Tenants.AnyAsync(row => row.Slug == "uncommitted-revision")).IsFalse();
        await Assert.That(await verification.Set<EventDiscoveryRevision>().IgnoreQueryFilters().CountAsync())
            .IsEqualTo(scenario is "unpaired" or "wrong-tenant" ? 0 : 1);
        await Assert.That(await verification.ConfigurationManifestOperations.AnyAsync()).IsFalse();
        await Assert.That(await verification.ConfigurationManifestTenantResults.IgnoreQueryFilters().AnyAsync()).IsFalse();
        await Assert.That(await verification.OutboxMessages.IgnoreQueryFilters().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task BootstrapRevision_FailedHistoryWriterRejectsAppliedProviderStatusAtItsOwnGuard()
    {
        await fixture.ResetAsync();
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        await using var original = await OpenRevisionGlobalAsync(host);
        var data = new ConfigurationManifestBootstrapData(original);
        var scope = RevisionScope(Guid.CreateVersion7(), Guid.CreateVersion7(), "failed-history-guard");
        Guid operationId = scope["Operation"]!.GetValue<Guid>();
        scope["Operations"]!.AsArray().Add(JsonValue.Create(operationId));
        string migration = fixture.NativeQueueEnrollment.MigratorLogin;
        string global = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global).Username!;
        DateTime occurred = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var operation = ConfigurationManifestOperation.Create(operationId, ConfigurationManifestAuditMode.Bootstrap,
            "v1alpha2", "ConfigurationManifest", "applied-is-not-failure-evidence",
            new string('a', ConfigurationManifestOperation.DigestLength), ConfigurationManifestOperationStatus.Applied,
            0, 0, 0, 0, null, null, occurred, occurred,
            new string('b', ConfigurationManifestOperation.DigestLength), 1);

        JsonObject before;
        PostgresException? rejection;
        await using (var transaction = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            before = await PostgresConfigurationManifestBootstrapContract.ReadValidationAsync(
                original, data, scope, migration, global, CancellationToken.None);
            var after = before.DeepClone().AsObject();
            var changes = new JsonArray();
            AddRevisionTestEffect(after, changes, "Operations", null, data.Row(operation));
            data.Order(after);

            rejection = await Assert.ThrowsAsync<PostgresException>(() => PostgresConfigurationManifestBootstrapContract.WriteEvidenceAsync(
                original, data, scope, migration, global, before, changes, after, CancellationToken.None));
            await transaction.RollbackAsync();
        }

        await using (var verification = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var observed = await PostgresConfigurationManifestBootstrapContract.ReadValidationAsync(
                original, data, scope, migration, global, CancellationToken.None);
            await Assert.That(ConfigurationManifestBootstrapData.Same(before, observed)).IsTrue();
            await verification.RollbackAsync();
        }
        await Assert.That((rejection!.SqlState, rejection.MessageText))
            .IsEqualTo((PostgresErrorCodes.InsufficientPrivilege, "manifest_bootstrap_failed_history_only"));
    }

    [Test]
    public async Task BootstrapRevision_OwnerHasOnlyFourColumnSelectAndInsertWithoutLockingOrMutationAuthority()
    {
        await fixture.ResetAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        var entity = migration.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string table = migration.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
        string[] columns = [.. entity.GetProperties().Select(property => property.GetColumnName(store)!)];
        await using var command = migration.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT (SELECT count(*) FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = @table::regclass AND a.attname = ANY (@columns)
                        AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.has_column_privilege(@role,a.attrelid,a.attnum,'SELECT')
                        AND pg_catalog.has_column_privilege(@role,a.attrelid,a.attnum,'INSERT')) = 4
                AND NOT pg_catalog.has_table_privilege(@role,@table,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                AND NOT pg_catalog.has_any_column_privilege(@role,@table,'UPDATE,REFERENCES')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid = @table::regclass AND acl.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = @role)
                        AND acl.is_grantable)
                AND (SELECT relrowsecurity AND relforcerowsecurity FROM pg_catalog.pg_class WHERE oid = @table::regclass)
            """;
        command.Parameters.Add(new NpgsqlParameter("table", NpgsqlDbType.Text) { Value = table });
        command.Parameters.Add(new NpgsqlParameter("role", NpgsqlDbType.Text) { Value = PostgresConfigurationManifestBootstrapContract.OwnerRole });
        command.Parameters.Add(new NpgsqlParameter("columns", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = columns });
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    private static async Task<ExploreDbContext> OpenRevisionGlobalAsync(ConfigurationManifestBootstrapTestHost host)
    {
        var context = await host.Services.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync();
        try
        {
            await context.Database.OpenConnectionAsync();
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private Task<JsonObject> ReadRevisionImageAsync(ExploreDbContext context, JsonObject scope) =>
        PostgresConfigurationManifestBootstrapContract.ReadAsync(context, new(context), scope,
            fixture.NativeQueueEnrollment.MigratorLogin,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global).Username!, CancellationToken.None);

    private static JsonObject RevisionScope(Guid tenantId, Guid revisionId, string slug) => new()
    {
        ["Slugs"] = new JsonArray(JsonValue.Create(slug)),
        ["Tenants"] = new JsonArray(JsonValue.Create(tenantId)),
        ["CreatedTenants"] = new JsonArray(JsonValue.Create(tenantId)),
        ["Revisions"] = new JsonArray(JsonValue.Create(revisionId)),
        ["Documents"] = new JsonArray(),
        ["Policies"] = new JsonArray(),
        ["Operations"] = new JsonArray(),
        ["Operation"] = JsonValue.Create(Guid.CreateVersion7()),
        ["Effect"] = JsonValue.Create(Guid.CreateVersion7())
    };

    private static void AddRevisionTestEffect(JsonObject image, JsonArray changes, string tag, JsonObject? before, JsonObject after)
    {
        changes.Add(new JsonObject { ["Tag"] = tag, ["Before"] = before?.DeepClone(), ["After"] = after.DeepClone() });
        var rows = image[tag]!.AsArray();
        if (before is not null)
            rows.Remove(rows.OfType<JsonObject>().Single(row => JsonNode.DeepEquals(row, before)));
        rows.Add(after.DeepClone());
    }

    private static Tenant NativeRevisionTenant(Guid id, string slug) => new()
    {
        Id = id,
        Slug = slug,
        FullName = slug,
        TenantStatusId = (int)TenantStatusEnum.Provisioning,
        TenantStatus = null!,
        CreatedAt = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)
    };
}
