using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Infrastructure;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

/// <summary>Native data capability, not a substitute for signed human custody.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class StorageConfigurationNativeContractTests(PostgreSqlContainerFixture fixture)
{
    private static readonly Guid Tenant = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");

    [Test]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task UnrelatedPurposesCannotObserveConfiguration(PrimaryDatabaseRole role)
    {
        await InstallAsync();
        await using var caller = Open(role);
        await caller.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalAbsenceAndSameValueReplacementInvalidateAcquisition(bool replacement)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string key = GovernanceKey;
        await migration.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        if (replacement)
        {
            migration.SystemSettings.Add(new() { SettingKey = key, Value = "\"\"" });
            await migration.SaveChangesAsync();
            migration.ChangeTracker.Clear();
        }
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        await migration.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        migration.SystemSettings.Add(new() { SettingKey = key, Value = "\"\"" });
        await migration.SaveChangesAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresStorageConfigurationContract.FunctionSql(caller, "acquire")}({{0}},{{1}})",
            Tenant, before.Revision));
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task GlobalCannotBypassFixedWriterWithScalarPrivileges()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        var sql = new HumanPrincipalSourceSql(caller);
        await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"UPDATE {sql.Table(typeof(SystemSetting))} SET {sql.Column(typeof(SystemSetting), "Value")} = 'null' WHERE false"));
    }

    [Test]
    public async Task RuntimeCannotSelectAnotherTenantSource()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Runtime);
        caller.TenantContext = new SelectedTenant(Tenant);
        await caller.Database.OpenConnectionAsync();
        var state = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        await Assert.That(state.Tenant.Count).IsEqualTo(13);
        await Assert.ThrowsAsync<PostgresException>(() => PostgresStorageConfigurationContract.ReadAsync(
            caller, fixture.NativeQueueEnrollment, Guid.CreateVersion7(), default));
    }

    [Test]
    public async Task OriginalSecretBindingAbsenceParticipatesBeforeAnyConfigurationWrite()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        const string key = SecretDefinitionRegistry.Keys.Storage.AccessKeyId;
        await migration.Set<SecretBinding>().Where(row => row.SettingKey == key && row.SettingScopeId == 1
            && row.ScopeId == null && row.Qualifier == "").ExecuteDeleteAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        migration.Add(SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null, "STORAGE_CONFIGURATION_TEST_REFERENCE"));
        await migration.SaveChangesAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresStorageConfigurationContract.FunctionSql(caller, "acquire")}({{0}},{{1}})", Tenant, before.Revision));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("metadata")]
    [Arguments("delete")]
    [Arguments("insert")]
    public async Task ChangesCommittedAfterSerializableSnapshotCannotBeAcquired(string change)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.SystemSettings.Where(row => row.SettingKey == GovernanceKey).ExecuteDeleteAsync();
        if (change != "insert")
        {
            migration.SystemSettings.Add(new() { SettingKey = GovernanceKey, Value = "\"\"" });
            await migration.SaveChangesAsync();
        }
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var before = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        if (change == "metadata")
            await migration.SystemSettings.Where(row => row.SettingKey == GovernanceKey)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Description, "concurrent configuration metadata"));
        else if (change == "delete")
            await migration.SystemSettings.Where(row => row.SettingKey == GovernanceKey).ExecuteDeleteAsync();
        else
        {
            migration.SystemSettings.Add(new() { SettingKey = GovernanceKey, Value = "\"\"" });
            await migration.SaveChangesAsync();
        }
        var exception = await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresStorageConfigurationContract.FunctionSql(caller, "acquire")}({{0}},{{1}})", Tenant, before.Revision));
        await Assert.That(exception!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task SourceExclusionRejectsConcurrentAcquisitionWithoutTimingAssumptions()
    {
        await InstallAsync();
        await using var first = Open(PrimaryDatabaseRole.Global);
        await using var second = Open(PrimaryDatabaseRole.Global);
        await first.Database.OpenConnectionAsync();
        await second.Database.OpenConnectionAsync();
        var source = await PostgresStorageConfigurationContract.ReadAsync(first, fixture.NativeQueueEnrollment, Tenant, default);
        await using var held = await first.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await first.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresStorageConfigurationContract.FunctionSql(first, "acquire")}({{0}},{{1}})", Tenant, source.Revision);
        // NOWAIT is an exact engine outcome, not a delay or a scheduling guess.
        await using var migration = fixture.CreateDbContext();
        await using var contender = await migration.Database.BeginTransactionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => migration.Database.ExecuteSqlRawAsync(
            $"SELECT id FROM {sql.Name(PostgresStorageConfigurationContract.SourceTable)} WHERE id = 1 FOR UPDATE NOWAIT"));
        await Assert.That(exception!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        await contender.RollbackAsync();
        await held.RollbackAsync();
        await using var admitted = await second.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await second.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresStorageConfigurationContract.FunctionSql(second, "acquire")}({{0}},{{1}})", Tenant, source.Revision);
        await admitted.RollbackAsync();
    }

    [Test]
    [Arguments("creation")]
    [Arguments("lock")]
    [Arguments("actor")]
    [Arguments("metadata")]
    [Arguments("precision")]
    [Arguments("incomplete-group")]
    [Arguments("missing-source")]
    [Arguments("foreign-target")]
    [Arguments("value-shape")]
    public async Task FixedNativeEffectsRejectTamperingAndRollback(string change)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        var effects = Effects(before);
        var effect = effects[0];
        effects[0] = change switch
        {
            "creation" => effect with { CreatedBy = Guid.CreateVersion7() },
            "lock" => effect with { IsLocked = true },
            "actor" => effect with { UpdatedBy = Guid.CreateVersion7() },
            "metadata" => effect with { Description = "foreign metadata" },
            "precision" => effect with { UpdatedAt = Instant.AddTicks(1) },
            "foreign-target" => effect with { TenantId = Tenant },
            "value-shape" => effect with { Value = "true" },
            _ => effect
        };
        var original = JsonSerializer.SerializeToNode(before)!.AsObject();
        if (change == "missing-source") original["System"]!.AsObject().Remove(GovernanceKey);
        if (change == "incomplete-group") effects = [effects[0]];
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<PostgresException>(() => RawWriteAsync(caller, original, effects));
            await transaction.RollbackAsync();
        }
        var after = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        await Assert.That(before.Same(after)).IsTrue();
    }

    [Test]
    public async Task FixedNativeGroupPreservesIdentityAndRollsBackEveryEffect()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        var effects = Effects(before);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await RawWriteAsync(caller, JsonSerializer.SerializeToNode(before)!.AsObject(), effects);
            var during = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
            foreach (string key in StorageConfigurationState.Keys)
                await Assert.That(during.System[key]).IsEqualTo(effects.SingleOrDefault(row => row.Key == key) ?? before.System[key]);
            await transaction.RollbackAsync();
        }
        var after = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        await Assert.That(before.Same(after)).IsTrue();
    }

    [Test]
    public async Task ConfigurationCommitAlsoAdvancesTheExistingProbeExclusion()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string probeAnchor = new HumanPrincipalSourceSql(migration).Name(PostgresStorageProbeSourceContract.SourceTable);
        long probeBefore = await migration.Database.SqlQueryRaw<long>(
            $"SELECT revision AS \"Value\" FROM {probeAnchor} WHERE id = 1").SingleAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await RawWriteAsync(caller, JsonSerializer.SerializeToNode(before)!.AsObject(), Effects(before));
            await transaction.CommitAsync();
        }
        long probeAfter = await migration.Database.SqlQueryRaw<long>(
            $"SELECT revision AS \"Value\" FROM {probeAnchor} WHERE id = 1").SingleAsync();
        await Assert.That(probeAfter > probeBefore).IsTrue();
    }

    [Test]
    [Arguments("public")]
    [Arguments("search-path")]
    [Arguments("invoker")]
    [Arguments("login")]
    [Arguments("disabled-trigger")]
    [Arguments("ambient-write")]
    [Arguments("owner-policy")]
    [Arguments("membership")]
    [Arguments("collation")]
    public async Task AdmissionAndReinstallationRejectDriftWithoutRepair(string change)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        var sql = new HumanPrincipalSourceSql(migration);
        string read = PostgresStorageConfigurationContract.FunctionSql(migration, "read") + "(uuid)";
        string system = sql.Table(typeof(SystemSetting));
        string global = sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string tenant = sql.Table(typeof(TenantSetting));
        (string alter, string restore) = change switch
        {
            "public" => ($"GRANT EXECUTE ON FUNCTION {read} TO PUBLIC", $"REVOKE EXECUTE ON FUNCTION {read} FROM PUBLIC"),
            "search-path" => ($"ALTER FUNCTION {read} SET search_path = public", $"ALTER FUNCTION {read} SET search_path = pg_catalog"),
            "invoker" => ($"ALTER FUNCTION {read} SECURITY INVOKER", $"ALTER FUNCTION {read} SECURITY DEFINER"),
            "login" => ($"ALTER ROLE {PostgresStorageConfigurationContract.OwnerRole} LOGIN",
                $"ALTER ROLE {PostgresStorageConfigurationContract.OwnerRole} NOLOGIN"),
            "disabled-trigger" => ($"ALTER TABLE {system} DISABLE TRIGGER storage_configuration_changed",
                $"ALTER TABLE {system} ENABLE ALWAYS TRIGGER storage_configuration_changed"),
            "ambient-write" => ($"GRANT UPDATE (value) ON {system} TO {global}", $"REVOKE UPDATE (value) ON {system} FROM {global}"),
            "owner-policy" => ($"ALTER POLICY storage_configuration_owner ON {tenant} USING (false)",
                $"ALTER POLICY storage_configuration_owner ON {tenant} USING (true)"),
            "membership" => ($"GRANT {PostgresStorageConfigurationContract.GlobalRole} TO {global} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
                $"GRANT {PostgresStorageConfigurationContract.GlobalRole} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            "collation" => ($"ALTER TABLE {system} ALTER COLUMN setting_key TYPE character varying(256) COLLATE \"C\"",
                $"ALTER TABLE {system} ALTER COLUMN setting_key TYPE character varying(256) COLLATE \"default\""),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await migration.Database.ExecuteSqlRawAsync(alter);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresStorageConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default));
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
    }

    [Test]
    public async Task MatchingSelfHashDoesNotReplaceIndependentRoutineBodyAdmission()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string signature = PostgresStorageConfigurationContract.FunctionSql(migration, "read") + "(uuid)";
        string body = await migration.Database.SqlQueryRaw<string>(
            "SELECT prosrc AS \"Value\" FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure({0})", signature).SingleAsync();
        string definition = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string comment = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        string altered = body + "\n";
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(altered)));
        await migration.Database.ExecuteSqlRawAsync(definition.Replace(body, altered, StringComparison.Ordinal));
        await migration.Database.ExecuteSqlRawAsync(
            $"COMMENT ON FUNCTION {signature} IS 'storage-configuration:v1:{hash}'");
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresStorageConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(definition);
            await migration.Database.ExecuteSqlRawAsync($"COMMENT ON FUNCTION {signature} IS '{comment.Replace("'", "''", StringComparison.Ordinal)}'");
        }
    }

    [Test]
    public async Task MissingAnchorIsRejectedWithoutRuntimeOrInstallerRepair()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string anchor = new HumanPrincipalSourceSql(migration).Name(PostgresStorageConfigurationContract.SourceTable);
        long revision = await migration.Database.SqlQueryRaw<long>($"SELECT revision AS \"Value\" FROM {anchor} WHERE id = 1").SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"DELETE FROM {anchor} WHERE id = 1");
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresStorageConfigurationContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment, Tenant));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresStorageConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(await migration.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM {anchor}").SingleAsync()).IsEqualTo(0);
        }
        finally { await migration.Database.ExecuteSqlRawAsync($"INSERT INTO {anchor} (id,revision) VALUES (1,{{0}})", revision); }
    }

    [Test]
    [Arguments("Id")]
    [Arguments("CreatedAt")]
    [Arguments("CreatedBy")]
    [Arguments("UpdatedAt")]
    [Arguments("UpdatedBy")]
    [Arguments("Description")]
    [Arguments("Category")]
    [Arguments("DisplayOrder")]
    [Arguments("AllowedValues")]
    [Arguments("ValueType")]
    [Arguments("IsLocked")]
    public async Task UnpatchedSourceColumnsAreNotReducedToValueEquality(string field)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string key = StorageConfigurationState.Keys[2];
        await migration.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        migration.SystemSettings.Add(new() { SettingKey = key, Value = "1024" });
        await migration.SaveChangesAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresStorageConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Tenant, default);
        var original = JsonSerializer.SerializeToNode(before)!.AsObject();
        var row = original["System"]![key]!;
        row[field] = field switch
        {
            "Id" or "CreatedBy" or "UpdatedBy" => JsonValue.Create(Guid.CreateVersion7()),
            "CreatedAt" or "UpdatedAt" => JsonValue.Create(Instant.AddDays(-11)),
            "ValueType" or "DisplayOrder" => JsonValue.Create(41),
            "IsLocked" => JsonValue.Create(true),
            _ => JsonValue.Create("replaced original metadata")
        };
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => RawWriteAsync(caller, original, Effects(before)));
        await transaction.RollbackAsync();
    }

    private static readonly DateTime Instant = new(2026, 10, 12, 0, 0, 1, DateTimeKind.Utc);
    private static readonly Guid Actor = Guid.Parse("018e4e5c-7f00-7000-8000-000000000031");
    private static StorageConfigurationRow[] Effects(StorageConfigurationState state) =>
        Enumerable.Range(6, 6).Select(index =>
        {
            string key = StorageConfigurationState.Keys[index];
            var before = state.System[key];
            var metadata = StorageSettingsMutation.Metadata(index);
            string value = index == 10 ? "true" : index == 11 ? "60" : "\"\"";
            return new StorageConfigurationRow(null, key, before?.Id ?? Guid.CreateVersion7(), value,
                metadata.Type, false, null, metadata.Description, metadata.Category, metadata.Order,
                before?.CreatedAt ?? Instant, before is null ? Actor : before.CreatedBy, Instant, Actor);
        }).ToArray();
    private static Task RawWriteAsync(ExploreDbContext caller, JsonObject before, StorageConfigurationRow[] effects) =>
        caller.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresStorageConfigurationContract.FunctionSql(caller, "write")}({{0}},{{1}},CAST({{2}} AS jsonb),CAST({{3}} AS jsonb),{{4}})",
            Tenant, Actor, before.ToJsonString(), JsonSerializer.Serialize(effects), Instant);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    private const string GovernanceKey = Explore.Domain.Constants.GovernanceSettingKeys.Storage.PublicEndpoint;

    private async Task InstallAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresStorageConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        return new(options.Options);
    }
}
