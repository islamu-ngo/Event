using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

/// <summary>Native data capability tests. These calls do not manufacture human authority.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class ResolverConfigurationNativeContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OtherPurposesCannotReadResolverSources(PrimaryDatabaseRole role)
    {
        await InstallAsync();
        await using var caller = Open(role);
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default));
    }

    [Test]
    public async Task EnrolledGlobalReadsAllSevenRowsWithoutAmbientWrites()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        var rows = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
        await Assert.That(rows.Count).IsEqualTo(7);
        await Assert.ThrowsAsync<PostgresException>(async () => await caller.Database.ExecuteSqlRawAsync(
            $"UPDATE {new HumanPrincipalSourceSql(caller).Table(typeof(SystemSetting))} SET value = 'false' WHERE setting_key = 'unrelated'"));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task EveryOriginalValidationRowParticipatesInSourceComparison(int index)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await SeedAsync(migration);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        var before = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
        await migration.SystemSettings.Where(row => row.SettingKey == ResolverConfigurationMutation.Keys[index])
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Description, "Changed original metadata"));
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => WriteAsync(caller, before, [Effect(before, 4)]));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("actor")]
    [Arguments("creation")]
    [Arguments("metadata")]
    [Arguments("lock")]
    [Arguments("unknown-key")]
    [Arguments("duplicate")]
    [Arguments("empty")]
    [Arguments("missing-source")]
    [Arguments("unknown-source")]
    [Arguments("precision")]
    [Arguments("source-precision")]
    [Arguments("isolation")]
    [Arguments("tenant")]
    public async Task FixedNativeWriteRejectsForeignOrUnrepresentableInputs(string change)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await SeedAsync(migration);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        var before = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
        var effect = Effect(before, 4);
        if (change == "actor") effect = effect with { UpdatedBy = Guid.CreateVersion7() };
        if (change == "creation") effect = effect with { CreatedBy = Guid.CreateVersion7() };
        if (change == "metadata") effect = effect with { Description = "Substituted" };
        if (change == "lock") effect = effect with { IsLocked = true };
        if (change == "unknown-key") effect = effect with { Key = "unrelated" };
        if (change == "precision") effect = effect with { UpdatedAt = effect.UpdatedAt!.Value.AddTicks(1) };
        var original = JsonSerializer.SerializeToNode(before)!.AsObject();
        if (change == "missing-source") original.Remove(ResolverConfigurationMutation.Keys[0]);
        if (change == "unknown-source") original["unrelated"] = null;
        if (change == "source-precision")
            original[ResolverConfigurationMutation.Keys[0]]!["CreatedAt"] = before[ResolverConfigurationMutation.Keys[0]]!.CreatedAt.AddTicks(1);
        await using var transaction = await caller.Database.BeginTransactionAsync(
            change == "isolation" ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable);
        if (change == "tenant")
            await caller.Database.ExecuteSqlRawAsync("SELECT pg_catalog.set_config('app.current_tenant_id', {0}, true)",
                Guid.CreateVersion7().ToString());
        ResolverConfigurationSettingSnapshot[] effects = change switch
        {
            "duplicate" => [effect, effect],
            "empty" => [],
            _ => [effect]
        };
        await Assert.ThrowsAsync<PostgresException>(() => RawWriteAsync(caller, original, effects));
        await transaction.RollbackAsync();
        var after = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
        foreach (var key in ResolverConfigurationMutation.Keys)
            await Assert.That(after[key]).IsEqualTo(before[key]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PartialPatchPreservesUnpatchedProjectionAndRollsBack(bool insert)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await SeedAsync(migration);
        if (insert)
            await migration.SystemSettings.Where(row => row.SettingKey == ResolverConfigurationMutation.Keys[4]).ExecuteDeleteAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        var before = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
        var effect = Effect(before, 4);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await WriteAsync(caller, before, [effect]);
            var provisional = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
            foreach (var key in ResolverConfigurationMutation.Keys)
                await Assert.That(provisional[key]).IsEqualTo(key == effect.Key ? effect : before[key]);
            await transaction.RollbackAsync();
        }
        var final = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
        foreach (var key in ResolverConfigurationMutation.Keys)
            await Assert.That(final[key]).IsEqualTo(before[key]);
    }

    [Test]
    [Arguments("public-execute")]
    [Arguments("search-path")]
    [Arguments("invoker")]
    [Arguments("owner-login")]
    [Arguments("extra-select")]
    [Arguments("executor-set")]
    [Arguments("missing-enrollment")]
    [Arguments("ambient-write")]
    [Arguments("disabled-trigger")]
    public async Task AdmissionAndReinstallRejectDriftWithoutRepair(string drift)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        var sql = new HumanPrincipalSourceSql(migration);
        string routine = PostgresResolverConfigurationContract.FunctionSql(migration, "read") + "()";
        string global = sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string table = sql.Table(typeof(SystemSetting));
        string change = drift switch
        {
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {routine} TO PUBLIC",
            "search-path" => $"ALTER FUNCTION {routine} SET search_path = public",
            "invoker" => $"ALTER FUNCTION {routine} SECURITY INVOKER",
            "owner-login" => $"ALTER ROLE {PostgresResolverConfigurationContract.OwnerRole} LOGIN",
            "extra-select" => $"GRANT SELECT ON {table} TO {PostgresResolverConfigurationContract.GlobalRole}",
            "executor-set" => $"GRANT {PostgresResolverConfigurationContract.GlobalRole} TO {global} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "missing-enrollment" => $"REVOKE {PostgresResolverConfigurationContract.GlobalRole} FROM {global}",
            "ambient-write" => $"GRANT UPDATE (value) ON {table} TO {global}",
            "disabled-trigger" => $"ALTER TABLE {table} DISABLE TRIGGER resolver_configuration_changed",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        string restore = drift switch
        {
            "public-execute" => $"REVOKE EXECUTE ON FUNCTION {routine} FROM PUBLIC",
            "search-path" => $"ALTER FUNCTION {routine} SET search_path = pg_catalog",
            "invoker" => $"ALTER FUNCTION {routine} SECURITY DEFINER",
            "owner-login" => $"ALTER ROLE {PostgresResolverConfigurationContract.OwnerRole} NOLOGIN",
            "extra-select" => $"REVOKE SELECT ON {table} FROM {PostgresResolverConfigurationContract.GlobalRole}",
            "ambient-write" => $"REVOKE UPDATE (value) ON {table} FROM {global}",
            "disabled-trigger" => $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER resolver_configuration_changed",
            _ => $"GRANT {PostgresResolverConfigurationContract.GlobalRole} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresResolverConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
        await PostgresResolverConfigurationContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment, default);
    }

    [Test]
    [Arguments("Key")]
    [Arguments("Id")]
    [Arguments("Value")]
    [Arguments("ValueType")]
    [Arguments("IsLocked")]
    [Arguments("AllowedValues")]
    [Arguments("Description")]
    [Arguments("Category")]
    [Arguments("DisplayOrder")]
    [Arguments("CreatedAt")]
    [Arguments("CreatedBy")]
    [Arguments("UpdatedAt")]
    [Arguments("UpdatedBy")]
    public async Task EveryOriginalColumnIsComparedEvenOnAnUnpatchedLeaf(string column)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await SeedAsync(migration);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        var before = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
        var supplied = JsonSerializer.SerializeToNode(before)!.AsObject();
        JsonNode source = supplied[ResolverConfigurationMutation.Keys[0]]!;
        source[column] = column switch
        {
            "Id" or "CreatedBy" or "UpdatedBy" => JsonValue.Create(Guid.CreateVersion7()),
            "ValueType" or "DisplayOrder" => JsonValue.Create(41),
            "IsLocked" => JsonValue.Create(false),
            "CreatedAt" or "UpdatedAt" => JsonValue.Create(Instant.AddDays(-2)),
            _ => JsonValue.Create("Replaced original column")
        };
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => RawWriteAsync(caller, supplied, [Effect(before, 4)]));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("insert")]
    [Arguments("update")]
    [Arguments("delete")]
    [Arguments("rename")]
    public async Task CommittedChangesAfterSerializableSnapshotInvalidateTheWholeSource(string change)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await SeedAsync(migration);
        string key = ResolverConfigurationMutation.Keys[6];
        if (change == "insert")
            await migration.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var before = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
        await using (var independent = fixture.CreateDbContext())
        {
            if (change == "insert")
            {
                independent.SystemSettings.Add(new()
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = "false",
                    ValueType = SettingValueType.Boolean,
                    CreatedAt = Instant
                });
                await independent.SaveChangesAsync();
            }
            else if (change == "update")
                await independent.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Description, "Committed outside the source snapshot"));
            else if (change == "delete")
                await independent.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            else
                await independent.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.SettingKey, "test.resolver.renamed"));
        }
        try
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => WriteAsync(caller, before, [Effect(before, 4)]));
            await Assert.That(failure!.SqlState).IsEqualTo("40001");
            await transaction.RollbackAsync();
        }
        finally
        {
            if (change == "rename")
                await migration.SystemSettings.Where(row => row.SettingKey == "test.resolver.renamed").ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task NativeWriteRetainsSourceExclusionUntilPhysicalRollback()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await SeedAsync(migration);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        var before = await PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await WriteAsync(caller, before, [Effect(before, 4)]);
        string anchor = new HumanPrincipalSourceSql(migration).Name("resolver_configuration_source");
        await using (var contender = fixture.CreateDbContext())
        {
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
                await contender.Database.ExecuteSqlRawAsync($"SELECT id FROM {anchor} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(failure!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
        }
        await transaction.RollbackAsync();
        await using var released = await migration.Database.BeginTransactionAsync();
        await migration.Database.ExecuteSqlRawAsync($"SELECT id FROM {anchor} WHERE id = 1 FOR UPDATE NOWAIT");
        await released.RollbackAsync();
    }

    [Test]
    [Arguments("table-owner")]
    [Arguments("schema-owner")]
    [Arguments("value-type")]
    [Arguments("unique-key")]
    [Arguments("function-owner")]
    [Arguments("anchor-column")]
    [Arguments("anchor-trigger")]
    public async Task CatalogShapeDriftFailsAdmission(string drift)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        var sql = new HumanPrincipalSourceSql(migration);
        await using var transaction = await migration.Database.BeginTransactionAsync();
        string change = drift switch
        {
            "table-owner" => $"ALTER TABLE {sql.Table(typeof(SystemSetting))} OWNER TO {PostgresResolverConfigurationContract.OwnerRole}",
            "schema-owner" => $"ALTER SCHEMA {sql.Quote(sql.Schema)} OWNER TO {PostgresResolverConfigurationContract.OwnerRole}",
            "value-type" => $"ALTER TABLE {sql.Table(typeof(SystemSetting))} ALTER COLUMN value TYPE varchar",
            "unique-key" => $"DROP INDEX {sql.Name(migration.Model.FindEntityType(typeof(SystemSetting))!.GetIndexes()
                .Single(index => index.IsUnique && index.Properties.Single().Name == nameof(SystemSetting.SettingKey)).GetDatabaseName()!)}",
            "function-owner" => $"ALTER FUNCTION {PostgresResolverConfigurationContract.FunctionSql(migration, "read")}() OWNER TO {sql.Quote(fixture.NativeQueueEnrollment.MigratorLogin)}",
            "anchor-column" => $"ALTER TABLE {sql.Name("resolver_configuration_source")} ADD COLUMN substitute boolean",
            "anchor-trigger" => $"ALTER TABLE {sql.Table(typeof(SystemSetting))} DISABLE TRIGGER resolver_configuration_truncate",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresResolverConfigurationContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment, default));
        await transaction.RollbackAsync();
        await PostgresResolverConfigurationContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment, default);
    }

    [Test]
    public async Task RestampedForeignBodyIsRejectedAgainstIndependentSourceHash()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string function = PostgresResolverConfigurationContract.FunctionSql(migration, "read");
        string signature = function + "()";
        string original = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string stamp = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION {function}() RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS 'BEGIN RETURN NULL; END';
            DO $stamp$ BEGIN
                EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L',
                    '{signature.Replace("'", "''", StringComparison.Ordinal)}',
                    'resolver-configuration:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex'))
                    FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure('{signature.Replace("'", "''", StringComparison.Ordinal)}'));
            END $stamp$;
            """);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresResolverConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(original);
            await migration.Database.ExecuteSqlRawAsync($"COMMENT ON FUNCTION {signature} IS '{stamp.Replace("'", "''", StringComparison.Ordinal)}'");
        }
    }

    [Test]
    public async Task TruncateParticipatesInResolverExclusionAndRollsBack()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await SeedAsync(migration);
        var names = new HumanPrincipalSourceSql(migration);
        string anchor = names.Name("resolver_configuration_source");
        await using var transaction = await migration.Database.BeginTransactionAsync();
        bool before = await migration.Database.SqlQueryRaw<bool>($"SELECT revision AS \"Value\" FROM {anchor} WHERE id = 1").SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"TRUNCATE {names.Table(typeof(SystemSetting))}");
        bool after = await migration.Database.SqlQueryRaw<bool>($"SELECT revision AS \"Value\" FROM {anchor} WHERE id = 1").SingleAsync();
        await Assert.That(after).IsEqualTo(!before);
        await Assert.That(await migration.SystemSettings.AnyAsync()).IsFalse();
        await transaction.RollbackAsync();
        await Assert.That(await migration.SystemSettings.AnyAsync()).IsTrue();
    }

    [Test]
    public async Task MissingExclusionAnchorRejectsReadAndReinstallWithoutReseeding()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string anchor = new HumanPrincipalSourceSql(migration).Name("resolver_configuration_source");
        bool revision = await migration.Database.SqlQueryRaw<bool>($"SELECT revision AS \"Value\" FROM {anchor} WHERE id = 1").SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"DELETE FROM {anchor} WHERE id = 1");
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresResolverConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresResolverConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(await migration.Database.SqlQueryRaw<int>($"SELECT count(*)::integer AS \"Value\" FROM {anchor}").SingleAsync())
                .IsEqualTo(0);
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync($"INSERT INTO {anchor} (id,revision) VALUES (1,{{0}})", revision);
        }
    }

    private async Task InstallAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresResolverConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        return new(options.Options);
    }

    private static readonly Guid ActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000031");
    private static readonly DateTime Instant = new(2026, 10, 12, 0, 0, 1, DateTimeKind.Utc);

    private static async Task SeedAsync(ExploreDbContext context)
    {
        var keys = ResolverConfigurationMutation.Keys.ToArray();
        await context.SystemSettings.Where(row => keys.Contains(row.SettingKey)).ExecuteDeleteAsync();
        for (int index = 0; index < keys.Length; index++)
            context.SystemSettings.Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = keys[index],
                Value = index == 4 ? "\"/original\"" : index == 5 ? "\"example.test\"" : "true",
                ValueType = index is 4 or 5 ? SettingValueType.String : SettingValueType.Boolean,
                Description = "Original metadata",
                Category = "Original",
                DisplayOrder = 89,
                IsLocked = true,
                CreatedAt = Instant.AddDays(-1),
                CreatedBy = null
            });
        await context.SaveChangesAsync();
    }

    private static ResolverConfigurationSettingSnapshot Effect(
        IReadOnlyDictionary<string, ResolverConfigurationSettingSnapshot?> before, int index)
    {
        string key = ResolverConfigurationMutation.Keys[index];
        var original = before[key];
        return new(key, original?.Id ?? Guid.CreateVersion7(), "\"/replacement\"", SettingValueType.String,
            false, null, "Path prefix used by the path-based tenant resolver.", "Routing", 24,
            original?.CreatedAt ?? Instant, original is null ? ActorId : original.CreatedBy, Instant, ActorId);
    }

    private static Task WriteAsync(ExploreDbContext caller,
        IReadOnlyDictionary<string, ResolverConfigurationSettingSnapshot?> before,
        ResolverConfigurationSettingSnapshot[] effects) =>
        RawWriteAsync(caller, JsonSerializer.SerializeToNode(before)!.AsObject(), effects);

    private static Task RawWriteAsync(ExploreDbContext caller, JsonObject before,
        ResolverConfigurationSettingSnapshot[] effects) =>
        caller.Database.ExecuteSqlRawAsync(
            $$"""
            SELECT {{PostgresResolverConfigurationContract.FunctionSql(caller, "write")}}
            ({0},CAST({1} AS jsonb),CAST({2} AS jsonb),{3})
            """,
            ActorId, before.ToJsonString(), JsonSerializer.Serialize(effects), Instant);
}
