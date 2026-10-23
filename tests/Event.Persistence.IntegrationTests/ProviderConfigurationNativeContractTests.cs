using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

/// <summary>Native data custody is tested separately from genuine request authority.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class ProviderConfigurationNativeContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task UnrelatedPurposesCannotReadProviderSources(PrimaryDatabaseRole role)
    {
        await InstallAsync();
        await using var caller = Open(role);
        await caller.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, null, default));
    }

    [Test]
    [Arguments("insert")]
    [Arguments("metadata")]
    [Arguments("replacement")]
    public async Task FirstSourceChangesCannotBeHiddenBySerializableSnapshot(string change)
    {
        await InstallAsync();
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        const string key = GovernanceSettingKeys.Authentication.GoogleClientId;
        await migration.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        if (change != "insert")
        {
            migration.SystemSettings.Add(new() { SettingKey = key, Value = "\"\"" });
            await migration.SaveChangesAsync();
            migration.ChangeTracker.Clear();
        }
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var before = await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, null, default);
        if (change == "metadata")
            await migration.SystemSettings.Where(row => row.SettingKey == key)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Description, "changed original metadata"));
        else
        {
            await migration.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            migration.SystemSettings.Add(new() { SettingKey = key, Value = "\"\"" });
            await migration.SaveChangesAsync();
        }
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresProviderConfigurationContract.AcquireAsync(caller, fixture.NativeQueueEnrollment, null, before, default));
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task AcquiredProviderSourceRemainsExcludedUntilPhysicalRollback()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, null, default);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await PostgresProviderConfigurationContract.AcquireAsync(caller, fixture.NativeQueueEnrollment, null, before, default);
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        string anchor = new HumanPrincipalSourceSql(migration).Name(PostgresProviderConfigurationContract.SourceTable);
        await using (var contender = await migration.Database.BeginTransactionAsync())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                migration.Database.ExecuteSqlRawAsync($"SELECT id FROM {anchor} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            await contender.RollbackAsync();
        }
        await transaction.RollbackAsync();
        await using var released = await migration.Database.BeginTransactionAsync();
        await migration.Database.ExecuteSqlRawAsync($"SELECT id FROM {anchor} WHERE id = 1 FOR UPDATE NOWAIT");
        await released.RollbackAsync();
    }

    [Test]
    public async Task GlobalCannotReplaceTheSelectedWriterWithAmbientScalarEffects()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, null, default);
        var sql = new HumanPrincipalSourceSql(caller);
        await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"UPDATE {sql.Table(typeof(SystemSetting))} SET {sql.Column(typeof(SystemSetting), "Value")} = 'null' WHERE false"));
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("participation")]
    [Arguments("event")]
    [Arguments("binding")]
    [Arguments("bootstrap")]
    [Arguments("user")]
    [Arguments("login")]
    public async Task EveryOriginalSourceRelationParticipatesInNativeExclusion(string source)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, null, default);
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        var sql = new HumanPrincipalSourceSql(migration);
        Type type = source switch
        {
            "tenant" => typeof(TenantSetting),
            "participation" => typeof(EventParticipationConfiguration),
            "event" => typeof(Explore.Domain.Event),
            "binding" => typeof(SecretBinding),
            "bootstrap" => typeof(InstanceBootstrapState),
            "user" => typeof(User),
            _ => typeof(UserExternalLogin)
        };
        // Whole-relation BEFORE STATEMENT exclusion must participate even for an empty
        // statement result. It is not dependent on existing rows in a selected range.
        await migration.Database.ExecuteSqlRawAsync($"UPDATE {sql.Table(type)} SET {sql.Column(type, "Id")} = {sql.Column(type, "Id")} WHERE false");
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresProviderConfigurationContract.AcquireAsync(caller, fixture.NativeQueueEnrollment, null, before, default));
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("identity")]
    [Arguments("creation")]
    [Arguments("metadata")]
    [Arguments("audit")]
    [Arguments("precision")]
    [Arguments("lock")]
    [Arguments("foreign-key")]
    [Arguments("missing-source")]
    [Arguments("incomplete-group")]
    public async Task FixedNativeProviderEffectsRejectTamperingAndRollbackEveryRow(string change)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Actor, default);
        var rows = NativeAuthorizationEffects(before);
        var row = rows[0];
        rows[0] = change switch
        {
            "identity" => row with { Id = Guid.Empty },
            "creation" => row with { CreatedBy = Guid.CreateVersion7() },
            "metadata" => row with { Description = "substituted" },
            "audit" => row with { UpdatedBy = Guid.CreateVersion7() },
            "precision" => row with { UpdatedAt = NativeInstant.AddTicks(1) },
            "lock" => row with { IsLocked = false },
            "foreign-key" => row with { Key = GovernanceSettingKeys.Authentication.GoogleClientId },
            _ => row
        };
        var raw = JsonNode.Parse(before.NativeImage!)!.AsObject();
        if (change == "missing-source") raw["System"]!.AsObject().Remove(GovernanceSettingKeys.Authentication.KeycloakAuthority);
        if (change == "incomplete-group") rows = [rows[1]];
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<PostgresException>(() => WriteNativeAuthorizationAsync(caller, raw.ToJsonString(), rows));
            await transaction.RollbackAsync();
        }
        var after = await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Actor, default);
        await Assert.That(before.Same(after)).IsTrue();
    }

    [Test]
    public async Task NativeProviderWritesPreserveIdentityMetadataAuditAndRollback()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Actor, default);
        var rows = NativeAuthorizationEffects(before);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await WriteNativeAuthorizationAsync(caller, before.NativeImage!, rows);
            var after = await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Actor, default);
            foreach (string key in ProviderConfigurationState.Keys)
                await Assert.That(after.System[key]).IsEqualTo(rows.SingleOrDefault(row => row.Key == key) ?? before.System[key]);
            await transaction.RollbackAsync();
        }
        await Assert.That(before.Same(await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Actor, default))).IsTrue();
    }

    [Test]
    public async Task ProviderWritesCoexistWithBothWholeRelationStorageTriggers()
    {
        await InstallAsync();
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        var sql = new HumanPrincipalSourceSql(migration);
        string probe = sql.Name(PostgresStorageProbeSourceContract.SourceTable);
        string configuration = sql.Name(PostgresStorageConfigurationContract.SourceTable);
        long beforeProbe = await migration.Database.SqlQueryRaw<long>($"SELECT revision AS \"Value\" FROM {probe} WHERE id = 1").SingleAsync();
        long beforeConfiguration = await migration.Database.SqlQueryRaw<long>($"SELECT revision AS \"Value\" FROM {configuration} WHERE id = 1").SingleAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment, Actor, default);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await WriteNativeAuthorizationAsync(caller, before.NativeImage!, NativeAuthorizationEffects(before));
            await transaction.CommitAsync();
        }
        await Assert.That(await migration.Database.SqlQueryRaw<long>($"SELECT revision AS \"Value\" FROM {probe} WHERE id = 1").SingleAsync()).IsGreaterThan(beforeProbe);
        await Assert.That(await migration.Database.SqlQueryRaw<long>($"SELECT revision AS \"Value\" FROM {configuration} WHERE id = 1").SingleAsync()).IsGreaterThan(beforeConfiguration);
    }

    [Test]
    [Arguments("trigger")]
    [Arguments("privilege")]
    [Arguments("role")]
    [Arguments("policy")]
    public async Task AdmissionAndInstallationRejectDriftWithoutRepair(string drift)
    {
        await InstallAsync();
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        var sql = new HumanPrincipalSourceSql(migration);
        string table = sql.Table(typeof(SystemSetting));
        string global = sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        (string corrupt, string restore) = drift switch
        {
            "trigger" => ($"ALTER TABLE {table} DISABLE TRIGGER provider_configuration_changed",
                $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER provider_configuration_changed"),
            "privilege" => ($"GRANT UPDATE ({sql.Column(typeof(SystemSetting), "Value")}) ON {table} TO {global}",
                $"REVOKE UPDATE ({sql.Column(typeof(SystemSetting), "Value")}) ON {table} FROM {global}"),
            "role" => ($"ALTER ROLE {PostgresProviderConfigurationContract.OwnerRole} INHERIT",
                $"ALTER ROLE {PostgresProviderConfigurationContract.OwnerRole} NOINHERIT"),
            _ => ($"ALTER POLICY provider_configuration_sources ON {sql.Table(typeof(TenantSetting))} USING (false)",
                $"ALTER POLICY provider_configuration_sources ON {sql.Table(typeof(TenantSetting))} USING (true)")
        };
        await migration.Database.ExecuteSqlRawAsync(corrupt);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() => PostgresProviderConfigurationContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
            await Assert.ThrowsAsync<PostgresException>(() => PostgresProviderConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
        await migration.Database.OpenConnectionAsync();
        await PostgresProviderConfigurationContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task MatchingSelfHashCannotSubstituteTheIndependentlyExpectedProviderBody()
    {
        await InstallAsync();
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        string function = PostgresProviderConfigurationContract.FunctionSql(migration, "read");
        string signature = function + "(uuid)";
        string definition = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_get_functiondef({0}::regprocedure) AS \"Value\"", signature).SingleAsync();
        string comment = await migration.Database.SqlQueryRaw<string>(
            "SELECT obj_description({0}::regprocedure,'pg_proc') AS \"Value\"", signature).SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION {function}(p_actor uuid) RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $changed$BEGIN RETURN NULL; END;$changed$;
            DO $stamp$ BEGIN EXECUTE 'COMMENT ON FUNCTION {signature} IS '
                || quote_literal('provider-configuration:v1:' || (SELECT encode(sha256(convert_to(prosrc,'UTF8')),'hex')
                    FROM pg_proc WHERE oid = '{signature}'::regprocedure)); END $stamp$;
            """);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() => PostgresProviderConfigurationContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
            await Assert.ThrowsAsync<PostgresException>(() => PostgresProviderConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(definition);
            await migration.Database.ExecuteSqlRawAsync($"COMMENT ON FUNCTION {signature} IS '{comment.Replace("'", "''", StringComparison.Ordinal)}'");
        }
    }

    [Test]
    public async Task MissingProviderAnchorCannotBeRepairedByAdmissionOrReinstallation()
    {
        await InstallAsync();
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        string anchor = new HumanPrincipalSourceSql(migration).Name(PostgresProviderConfigurationContract.SourceTable);
        long revision = await migration.Database.SqlQueryRaw<long>($"SELECT revision AS \"Value\" FROM {anchor} WHERE id = 1").SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"DELETE FROM {anchor} WHERE id = 1");
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() => PostgresProviderConfigurationContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
            await Assert.ThrowsAsync<PostgresException>(() => PostgresProviderConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(await migration.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM {anchor}").SingleAsync()).IsEqualTo(0);
        }
        finally { await migration.Database.ExecuteSqlRawAsync($"INSERT INTO {anchor}(id,revision) VALUES(1,{{0}})", revision); }
    }

    private static readonly Guid Actor = Guid.Parse("018e4e5c-7f00-7000-8000-000000000032");
    private static readonly DateTime NativeInstant = new(2026, 10, 12, 10, 30, 0, DateTimeKind.Utc);

    private static ProviderConfigurationRow[] NativeAuthorizationEffects(ProviderConfigurationState state) =>
        new[] { GovernanceSettingKeys.Security.AuthorizationProvider, GovernanceSettingKeys.Cerbos.GrpcEndpoint }
            .Select((key, index) =>
            {
                var original = state.System[key];
                return new ProviderConfigurationRow(key, original?.Id ?? Guid.CreateVersion7(), index == 0 ? "\"local\"" : "\"\"",
                    SettingValueType.String, true, null,
                    index == 0 ? "Authorization provider: 'cerbos' for external PDP, 'local' for built-in RBAC" : "Cerbos PDP gRPC endpoint for authorization requests",
                    "Security", index + 1, original?.CreatedAt ?? NativeInstant, original is null ? Actor : original.CreatedBy, NativeInstant, Actor);
            }).ToArray();

    private static Task WriteNativeAuthorizationAsync(ExploreDbContext caller, string before, ProviderConfigurationRow[] rows) =>
        caller.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresProviderConfigurationContract.FunctionSql(caller, "authorization_human")}({{0}},CAST({{1}} AS jsonb),CAST({{2}} AS jsonb),{{3}})",
            Actor, before, JsonSerializer.Serialize(rows), NativeInstant);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeHumanAndSetupEntrypointsCannotSubstituteOriginalSourceImages(bool setupEntry)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await PostgresProviderConfigurationContract.ReadAsync(caller, fixture.NativeQueueEnrollment,
            setupEntry ? Actor : null, default);
        var rows = NativeAuthorizationEffects(before);
        if (setupEntry)
            rows = rows.Select(row => row with
            {
                CreatedBy = before.System[row.Key] is { } original ? original.CreatedBy : null,
                UpdatedBy = null
            }).ToArray();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var failure = setupEntry
            ? await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
                $"SELECT {PostgresProviderConfigurationContract.FunctionSql(caller, "authorization_setup")}(CAST({{0}} AS jsonb),CAST({{1}} AS jsonb),{{2}})",
                before.NativeImage!, JsonSerializer.Serialize(rows), NativeInstant))
            : await Assert.ThrowsAsync<PostgresException>(() => WriteNativeAuthorizationAsync(caller, before.NativeImage!, rows));
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
        await transaction.RollbackAsync();
    }

    private async Task InstallAsync()
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        await PostgresProviderConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        return new(options.Options);
    }
}
