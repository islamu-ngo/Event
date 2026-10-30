using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

/// <summary>Native source exclusion, not a substitute for signed original-action authority.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class StorageProbeNativeContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("insert")]
    [Arguments("metadata")]
    [Arguments("delete")]
    [Arguments("replacement")]
    [Arguments("absent-binding")]
    public async Task CommittedSourceChangesInvalidateAnAlreadyStartedSnapshot(string change)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        string key = GovernanceSettingKeys.Storage.Endpoint;
        await migration.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        if (change != "insert")
        {
            migration.SystemSettings.Add(new() { SettingKey = key, Value = "\"https://probe.example.test\"" });
            await migration.SaveChangesAsync();
        }
        string secretKey = SecretDefinitionRegistry.Keys.Storage.AccessKeyId;
        await migration.SecretBindings.Where(row => row.SettingKey == secretKey).ExecuteDeleteAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default);
        await using (var writer = fixture.CreateDbContext())
        {
            if (change is "delete" or "replacement")
                await writer.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            if (change is "insert" or "replacement")
            {
                writer.SystemSettings.Add(new() { SettingKey = key, Value = "\"https://probe.example.test\"" });
                await writer.SaveChangesAsync();
            }
            if (change == "metadata")
                await writer.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Description, "Changed source metadata"));
            if (change == "absent-binding")
            {
                writer.SecretBindings.Add(SecretBinding.CreateEnvironmentVariable(
                    secretKey, SecretScope.Instance, null, "PROBE_NATIVE_ACCESS"));
                await writer.SaveChangesAsync();
            }
        }
        var failure = await Assert.ThrowsAsync<PostgresException>(() => AcquireAsync(caller));
        await Assert.That(failure!.SqlState).IsEqualTo("40001");
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task AcquisitionRetainsGenerationAndExclusionUntilPhysicalRollback()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        long original = await PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await AcquireAsync(caller)).IsEqualTo(original);
        await using var contender = fixture.CreateDbContext();
        string anchor = new HumanPrincipalSourceSql(contender).Name(PostgresStorageProbeSourceContract.SourceTable);
        await using (var competing = await contender.Database.BeginTransactionAsync())
        {
            var blocked = await Assert.ThrowsAsync<PostgresException>(async () =>
                await contender.Database.ExecuteSqlRawAsync($"SELECT id FROM {anchor} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(blocked!.SqlState).IsEqualTo("55P03");
            await competing.RollbackAsync();
        }
        await transaction.RollbackAsync();
        await using (var released = await contender.Database.BeginTransactionAsync())
        {
            await contender.Database.ExecuteSqlRawAsync($"SELECT id FROM {anchor} WHERE id = 1 FOR UPDATE NOWAIT");
            await released.RollbackAsync();
        }
        await Assert.That(await PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default))
            .IsEqualTo(original);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SourceGenerationTracksOnlyCommittedChanges(bool commit)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        long before = await PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default);
        await using (var writer = fixture.CreateDbContext())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync();
            writer.SystemSettings.Add(new() { SettingKey = $"probe.rollback.{Guid.CreateVersion7():N}", Value = "false" });
            await writer.SaveChangesAsync();
            if (commit) await transaction.CommitAsync();
            else await transaction.RollbackAsync();
        }
        long after = await PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default);
        await Assert.That(after > before).IsEqualTo(commit);
        if (!commit) await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    [Arguments("system")]
    [Arguments("tenant")]
    [Arguments("binding")]
    [Arguments("bootstrap")]
    public async Task EveryFixedSourceRangeParticipatesEvenWhenTheMatchedRangeIsAbsent(string source)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        long before = await PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default);
        await using var writer = fixture.CreateDbContext();
        Type type = source switch
        {
            "system" => typeof(SystemSetting),
            "tenant" => typeof(TenantSetting),
            "binding" => typeof(SecretBinding),
            "bootstrap" => typeof(InstanceBootstrapState),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        var names = new HumanPrincipalSourceSql(writer);
        string id = names.Column(type, "Id");
        await writer.Database.ExecuteSqlRawAsync($"UPDATE {names.Table(type)} SET {id} = {id} WHERE false");
        long after = await PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default);
        await Assert.That(after).IsEqualTo(before + 1);
    }

    [Test]
    public async Task TruncateParticipatesAndRollbackRestoresItsSourceGeneration()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        long before = await PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default);
        await using var writer = fixture.CreateDbContext();
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            var names = new HumanPrincipalSourceSql(writer);
            await writer.Database.ExecuteSqlRawAsync($"TRUNCATE {names.Table(typeof(TenantSetting))}");
            long provisional = await writer.Database.SqlQueryRaw<long>(
                $"SELECT revision AS \"Value\" FROM {names.Name(PostgresStorageProbeSourceContract.SourceTable)} WHERE id = 1").SingleAsync();
            await Assert.That(provisional).IsEqualTo(before + 1);
            await transaction.RollbackAsync();
        }
        await Assert.That(await PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default))
            .IsEqualTo(before);
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task ForeignOrUnboundSelectionsCannotBorrowTheGlobalFence(PrimaryDatabaseRole role)
    {
        await InstallAsync();
        await using var caller = Open(role);
        await caller.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresStorageProbeSourceContract.ObserveAsync(caller, fixture.NativeQueueEnrollment, default));
    }

    [Test]
    [Arguments("public-execute")]
    [Arguments("search-path")]
    [Arguments("owner-login")]
    [Arguments("executor-set")]
    [Arguments("missing-anchor")]
    [Arguments("disabled-trigger")]
    [Arguments("fingerprint-collation")]
    [Arguments("body")]
    [Arguments("body-and-stamp")]
    public async Task ReadyAndReinstallRejectDriftWithoutRepair(string drift)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        var sql = new HumanPrincipalSourceSql(migration);
        string routine = PostgresStorageProbeSourceContract.FunctionSql(migration, "observe") + "(uuid)";
        await migration.Database.OpenConnectionAsync();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        string change = drift switch
        {
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {routine} TO PUBLIC",
            "search-path" => $"ALTER FUNCTION {routine} SET search_path = public",
            "owner-login" => $"ALTER ROLE {PostgresStorageProbeSourceContract.OwnerRole} LOGIN",
            "executor-set" => $"GRANT {PostgresStorageProbeSourceContract.GlobalRole} TO {sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "missing-anchor" => $"DELETE FROM {sql.Name(PostgresStorageProbeSourceContract.SourceTable)} WHERE id = 1",
            "disabled-trigger" => $"ALTER TABLE {sql.Table(typeof(SecretBinding))} DISABLE TRIGGER storage_probe_source_changed",
            "fingerprint-collation" => $"ALTER TABLE {sql.Table(typeof(InstanceBootstrapState))} ALTER COLUMN completed_identity_fingerprint TYPE character(64) COLLATE \"default\"",
            "body" or "body-and-stamp" => $"CREATE OR REPLACE FUNCTION {routine.Replace("(uuid)", "(p_tenant uuid)", StringComparison.Ordinal)} RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS 'BEGIN RETURN 0; END;'",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        if (drift == "body-and-stamp")
            await migration.Database.ExecuteSqlRawAsync($"""
                DO $drift$ DECLARE stamp text; BEGIN
                    SELECT 'storage-probe-source:v1:' || pg_catalog.encode(
                        pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex') INTO stamp
                    FROM pg_catalog.pg_proc WHERE oid = '{routine}'::regprocedure;
                    EXECUTE pg_catalog.format('COMMENT ON FUNCTION {routine} IS %L',stamp);
                END $drift$;
                """);
        // Savepoints preserve the drift so the installer sees the same uncorrected catalog.
        await transaction.CreateSavepointAsync("ready");
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresStorageProbeSourceContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment, default));
        await transaction.RollbackToSavepointAsync("ready");
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresStorageProbeSourceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await transaction.RollbackAsync();
        await PostgresStorageProbeSourceContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment, default);
    }

    private async Task InstallAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresStorageProbeSourceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        return new(options.Options);
    }

    private Task<long> AcquireAsync(ExploreDbContext caller) =>
        caller.Database.SqlQueryRaw<long>(
            $"SELECT {PostgresStorageProbeSourceContract.FunctionSql(caller, "acquire")}(NULL::uuid) AS \"Value\"").SingleAsync();
}
