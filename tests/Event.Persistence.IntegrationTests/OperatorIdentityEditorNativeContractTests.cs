using System.Data;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Exercises the enrolled data capability, not HTTP administrator or setup authority.
/// Positive original-request custody belongs to the operator authentication tests.
/// </summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class OperatorIdentityEditorNativeContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task EnrolledGlobalCanObserveButCannotWriteAnOrdinaryScalar()
    {
        await InstallAsync();
        await using var global = Open(PrimaryDatabaseRole.Global);
        await PostgresOperatorIdentityEditorContract.ReadSettingAsync(global, fixture.NativeQueueEnrollment, default);
        await Assert.ThrowsAsync<PostgresException>(async () => await global.Database.ExecuteSqlRawAsync(
            $"UPDATE {new HumanPrincipalSourceSql(global).Table(typeof(SystemSetting))} SET value = 'false' WHERE setting_key = 'unrelated'"));
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OtherEnrolledPurposesCannotEnterEditor(PrimaryDatabaseRole role)
    {
        await InstallAsync();
        await using var caller = Open(role);
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresOperatorIdentityEditorContract.ReadSettingAsync(caller, fixture.NativeQueueEnrollment, default));
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
    public async Task AdmissionRejectsDriftWithoutRepairingIt(string drift)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string routine = PostgresOperatorIdentityEditorContract.FunctionSql(migration, "read") + "()";
        var names = new HumanPrincipalSourceSql(migration);
        string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string change = drift switch
        {
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {routine} TO PUBLIC",
            "search-path" => $"ALTER FUNCTION {routine} SET search_path = public",
            "invoker" => $"ALTER FUNCTION {routine} SECURITY INVOKER",
            "owner-login" => $"ALTER ROLE {PostgresOperatorIdentityEditorContract.OwnerRole} LOGIN",
            "extra-select" => $"GRANT SELECT ON {names.Table(typeof(SystemSetting))} TO {PostgresOperatorIdentityEditorContract.GlobalRole}",
            "executor-set" => $"GRANT {PostgresOperatorIdentityEditorContract.GlobalRole} TO {global} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "missing-enrollment" => $"REVOKE {PostgresOperatorIdentityEditorContract.GlobalRole} FROM {global}",
            "ambient-write" => $"GRANT UPDATE (value) ON {names.Table(typeof(SystemSetting))} TO {global}",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        string restore = drift switch
        {
            "public-execute" => $"REVOKE EXECUTE ON FUNCTION {routine} FROM PUBLIC",
            "search-path" => $"ALTER FUNCTION {routine} SET search_path = pg_catalog",
            "invoker" => $"ALTER FUNCTION {routine} SECURITY DEFINER",
            "owner-login" => $"ALTER ROLE {PostgresOperatorIdentityEditorContract.OwnerRole} NOLOGIN",
            "extra-select" => $"REVOKE SELECT ON {names.Table(typeof(SystemSetting))} FROM {PostgresOperatorIdentityEditorContract.GlobalRole}",
            "ambient-write" => $"REVOKE UPDATE (value) ON {names.Table(typeof(SystemSetting))} FROM {global}",
            _ => $"GRANT {PostgresOperatorIdentityEditorContract.GlobalRole} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresOperatorIdentityEditorContract.ReadSettingAsync(caller, fixture.NativeQueueEnrollment, default));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresOperatorIdentityEditorContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
        await PostgresOperatorIdentityEditorContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment, default);
    }

    [Test]
    [Arguments("table-owner")]
    [Arguments("schema-owner")]
    [Arguments("value-type")]
    [Arguments("setting-key")]
    [Arguments("function-owner")]
    public async Task OriginalSourceStructureIsAttestedBeforeAnyEffect(string drift)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await PostgresOperatorIdentityEditorContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment, default);
        var names = new HumanPrincipalSourceSql(migration);
        await using var transaction = await migration.Database.BeginTransactionAsync();
        string change = drift switch
        {
            "table-owner" => $"ALTER TABLE {names.Table(typeof(SystemSetting))} OWNER TO {PostgresOperatorIdentityEditorContract.OwnerRole}",
            "schema-owner" => $"ALTER SCHEMA {names.Quote(names.Schema)} OWNER TO {PostgresOperatorIdentityEditorContract.OwnerRole}",
            "value-type" => $"ALTER TABLE {names.Table(typeof(SystemSetting))} ALTER COLUMN value TYPE varchar",
            "setting-key" => $"DROP INDEX {names.Name(migration.Model.FindEntityType(typeof(SystemSetting))!.GetIndexes()
                .Single(index => index.IsUnique && index.Properties.Single().Name == nameof(SystemSetting.SettingKey)).GetDatabaseName()!)}",
            "function-owner" => $"ALTER FUNCTION {PostgresOperatorIdentityEditorContract.FunctionSql(migration, "read")}() OWNER TO {names.Quote(fixture.NativeQueueEnrollment.MigratorLogin)}",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresOperatorIdentityEditorContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment, default));
        // PostgreSQL transactional DDL restores original owners and ACLs as well as data.
        await transaction.RollbackAsync();
        await PostgresOperatorIdentityEditorContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment, default);
    }

    [Test]
    public async Task NativeWritePreservesMetadataAndRollsBackWithItsAudit()
    {
        await InstallAsync();
        await using var seed = fixture.CreateDbContext();
        var before = await SeedAsync(seed);
        var after = before with
        {
            Value = before.Value.Replace("Original", "Replacement", StringComparison.Ordinal)
                .Replace("018e4e5c-7f00-7000-8000-000000000010", "018e4e5c-7f00-7000-8000-000000000011", StringComparison.Ordinal),
            UpdatedAt = new DateTime(2026, 10, 12, 0, 0, 1, DateTimeKind.Utc).AddTicks(1234560),
            UpdatedBy = Guid.Parse("018e4e5c-7f00-7000-8000-000000000012")
        };
        Guid auditId = Guid.CreateVersion7();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await NativeWriteAsync(caller, before, after, auditId);
            var provisional = await PostgresOperatorIdentityEditorContract.ReadSettingAsync(caller, fixture.NativeQueueEnrollment, default);
            await Assert.That(provisional).IsEqualTo(after);
            var audit = await PostgresOperatorIdentityEditorContract.ReadAuditAsync(caller, fixture.NativeQueueEnrollment, auditId, default);
            await Assert.That(audit!.CreatedAt).IsEqualTo(after.UpdatedAt!.Value);
            await Assert.That(audit.Status).IsEqualTo(OutboxMessageStatus.Pending);
            await transaction.RollbackAsync();
        }
        seed.ChangeTracker.Clear();
        await Assert.That(OperatorIdentitySettingSnapshot.From(await seed.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.Id == before.Id))).IsEqualTo(before);
        await Assert.That(await seed.OutboxMessages.AnyAsync(row => row.Id == auditId)).IsFalse();
    }

    [Test]
    [Arguments("actor")]
    [Arguments("metadata")]
    [Arguments("lock")]
    [Arguments("source")]
    [Arguments("isolation")]
    [Arguments("tenant")]
    [Arguments("key")]
    [Arguments("official")]
    [Arguments("audit-actor")]
    [Arguments("unknown-audit")]
    [Arguments("precision")]
    public async Task NativeWriteRejectsChangedActorEffectSourceOrPhysicalScope(string change)
    {
        await InstallAsync();
        await using var seed = fixture.CreateDbContext();
        var before = await SeedAsync(seed);
        var after = before with
        {
            Value = before.Value.Replace("018e4e5c-7f00-7000-8000-000000000010", "018e4e5c-7f00-7000-8000-000000000011", StringComparison.Ordinal),
            UpdatedAt = before.CreatedAt.AddSeconds(1),
            UpdatedBy = Guid.Parse("018e4e5c-7f00-7000-8000-000000000012")
        };
        if (change == "metadata") after = after with { Description = "Substituted" };
        if (change == "lock") after = after with { IsLocked = false };
        if (change == "precision") after = after with { UpdatedAt = after.UpdatedAt!.Value.AddTicks(1) };
        if (change == "official") after = after with
        {
            Value = after.Value.Replace("\"isOfficialInstance\":false", "\"isOfficialInstance\":true", StringComparison.Ordinal)
        };
        var selectedBefore = change == "source" ? before with { Value = "{}" } : before;
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await using var transaction = await caller.Database.BeginTransactionAsync(
            change == "isolation" ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable);
        if (change == "tenant")
            await caller.Database.ExecuteSqlRawAsync("SELECT pg_catalog.set_config('app.current_tenant_id', {0}, true)",
                Guid.CreateVersion7().ToString());
        Guid auditId = Guid.CreateVersion7();
        await Assert.ThrowsAsync<PostgresException>(() =>
            NativeWriteAsync(caller, selectedBefore, after, auditId, change == "actor" ? Guid.CreateVersion7() : null, change));
        await transaction.RollbackAsync();
        seed.ChangeTracker.Clear();
        await Assert.That(OperatorIdentitySettingSnapshot.From(await seed.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.Id == before.Id))).IsEqualTo(before);
        await Assert.That(await seed.OutboxMessages.AnyAsync(row => row.Id == auditId)).IsFalse();
    }

    [Test]
    public async Task RestampedForeignBodyCannotReplaceTheSourceDefinedRead()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string function = PostgresOperatorIdentityEditorContract.FunctionSql(migration, "read");
        string signature = function + "()";
        string original = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        string stamp = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.obj_description(pg_catalog.to_regprocedure({0}),'pg_proc') AS \"Value\"", signature).SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION {function}() RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS 'BEGIN RETURN NULL; END';
            DO $stamp$ BEGIN
                EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L',
                    '{signature.Replace("'", "''", StringComparison.Ordinal)}',
                    'operator-identity-editor:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex'))
                    FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure('{signature.Replace("'", "''", StringComparison.Ordinal)}'));
            END $stamp$;
            """);
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresOperatorIdentityEditorContract.ReadSettingAsync(caller, fixture.NativeQueueEnrollment, default));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresOperatorIdentityEditorContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(original);
            await migration.Database.ExecuteSqlRawAsync($"COMMENT ON FUNCTION {signature} IS '{stamp.Replace("'", "''", StringComparison.Ordinal)}'");
        }
    }

    [Test]
    public async Task PendingSourceCapabilityLocksTheOriginalHighestGenerationWithoutSetupAuthority()
    {
        await InstallAsync();
        await using var seed = fixture.CreateDbContext();
        long generation = (await seed.InstanceBootstrapStates.MaxAsync(row => (long?)row.Generation) ?? 0) + 1;
        var pending = InstanceBootstrapState.CreateConfiguredAdministratorPending(
            Guid.CreateVersion7(), AuthenticationProviderKind.Keycloak, DeploymentMode.SingleTenant,
            generation, new string('a', 64), new string('b', 64), DateTime.UtcNow);
        seed.InstanceBootstrapStates.Add(pending);
        await seed.SaveChangesAsync();
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            string? original = await PostgresOperatorIdentityEditorContract.ReadPendingAsync(caller, fixture.NativeQueueEnrollment, false, default);
            using var source = JsonDocument.Parse(original!);
            await Assert.That(source.RootElement.GetProperty("Id").GetGuid()).IsEqualTo(pending.Id);
            await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await Assert.That(await PostgresOperatorIdentityEditorContract.ReadPendingAsync(
                    caller, fixture.NativeQueueEnrollment, true, default)).IsEqualTo(original);
                await using var contender = fixture.CreateDbContext();
                await using var attempt = await contender.Database.BeginTransactionAsync();
                var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await contender.Database.ExecuteSqlRawAsync(
                        $"SELECT id FROM {new HumanPrincipalSourceSql(contender).Table(typeof(InstanceBootstrapState))} WHERE id = {{0}} FOR UPDATE NOWAIT",
                        pending.Id));
                await Assert.That(denied!.SqlState).IsEqualTo("55P03");
                await attempt.RollbackAsync();
                await transaction.RollbackAsync();
            }
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresOperatorIdentityEditorContract.ReadPendingAsync(caller, fixture.NativeQueueEnrollment, true, default));
        }
        finally
        {
            await seed.InstanceBootstrapStates.Where(row => row.Id == pending.Id).ExecuteDeleteAsync();
        }
    }

    private async Task InstallAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresOperatorIdentityEditorContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        return new ExploreDbContext(options.Options);
    }

    private static async Task<OperatorIdentitySettingSnapshot> SeedAsync(ExploreDbContext seed)
    {
        await seed.SystemSettings.Where(row => row.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity).ExecuteDeleteAsync();
        var row = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
            Value = """{"operatorId":"018e4e5c-7f00-7000-8000-000000000009","revision":"018e4e5c-7f00-7000-8000-000000000010","isOfficialInstance":false,"publicName":"Original"}""",
            ValueType = SettingValueType.Json,
            IsLocked = true,
            Description = "Original metadata",
            Category = "OperatorIdentity",
            DisplayOrder = 17,
            CreatedAt = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc)
        };
        seed.SystemSettings.Add(row);
        await seed.SaveChangesAsync();
        return OperatorIdentitySettingSnapshot.From(row);
    }

    private static async Task NativeWriteAsync(ExploreDbContext caller, OperatorIdentitySettingSnapshot before,
        OperatorIdentitySettingSnapshot after, Guid auditId, Guid? wrongActor = null, string? change = null)
    {
        // A raw native data call deliberately makes no assertion of a human credential.
        var payload = JsonSerializer.SerializeToNode(new
        {
            ActorUserId = change == "audit-actor" ? Guid.CreateVersion7() : after.UpdatedBy,
            ContentDigest = "digest",
            ExpectedRevisionHash = "revision",
            CommittedRevision = Guid.Parse("018e4e5c-7f00-7000-8000-000000000011")
        })!;
        var afterJson = JsonSerializer.SerializeToNode(after)!;
        if (change == "key") afterJson["SettingKey"] = "unrelated";
        if (change == "unknown-audit") payload["Unknown"] = "not in the original effect";
        await caller.Database.ExecuteSqlRawAsync(
            $$"""
            SELECT {{PostgresOperatorIdentityEditorContract.FunctionSql(caller, "write")}}
            ({0},CAST({1} AS jsonb),CAST({2} AS jsonb),{3},CAST({4} AS jsonb)::text,{5},{6})
            """,
            wrongActor ?? after.UpdatedBy!.Value, JsonSerializer.Serialize(before), afterJson.ToJsonString(),
            auditId, payload.ToJsonString(), after.UpdatedAt!.Value, true);
    }
}
