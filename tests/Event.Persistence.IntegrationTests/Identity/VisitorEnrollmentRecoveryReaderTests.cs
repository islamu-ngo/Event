using System.Collections.Immutable;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Identity;

/// <summary>Exercises the actual configured Global login, never SET ROLE or a Runtime writer.</summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class VisitorEnrollmentRecoveryReaderTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ActualGlobalReaderReturnsOnlyEligibleMetadataAcrossTenants()
    {
        await fixture.ResetAsync();
        await using var migration = OpenMigrator();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var seed = fixture.CreateDbContext();
        seed.BindTenantLease();
        var expected = await VisitorRecoveryRows.SeedStagesAsync(seed);
        var reader = new VisitorEnrollmentRecoveryReader(GlobalFactory(), fixture.NativeQueueEnrollment);

        var page = await reader.ReadCandidatesAsync(VisitorRecoveryRows.Now, 100);

        await Assert.That(page.Length).IsEqualTo(6);
        for (int index = 0; index < expected.Length; index++)
            await Assert.That(page[index]).IsEqualTo(expected[index]);
    }

    [Test]
    public async Task InstalledGlobalAuthorityHasExactlySixNonGrantableMetadataColumns()
    {
        await using var migration = OpenMigrator();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        await using var command = migration.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COALESCE(string_agg(a.attname,'|' ORDER BY a.attname),'')
            FROM pg_catalog.pg_attribute a
            WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                AND pg_catalog.has_column_privilege(@login,a.attrelid,a.attnum,'SELECT')
            """;
        command.Parameters.Add(new NpgsqlParameter("table", names.Table(typeof(VisitorEnrollmentOperation))));
        command.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
        await Assert.That(await command.ExecuteScalarAsync())
            .IsEqualTo("cleanup_completed_at|expires_at|generation|id|stage|tenant_id");

        command.CommandText = """
            SELECT NOT pg_catalog.has_table_privilege(@login,pg_catalog.to_regclass(@table),
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN (VALUES ('INSERT'),('UPDATE'),('REFERENCES'),('SELECT WITH GRANT OPTION')) permission(privilege)
                    WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                        AND pg_catalog.has_column_privilege(@login,a.attrelid,a.attnum,permission.privilege))
            """;
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    [Test]
    [Arguments(nameof(VisitorEnrollmentOperation.IntentDigest))]
    [Arguments(nameof(VisitorEnrollmentOperation.LocalSubjectId))]
    [Arguments(nameof(VisitorEnrollmentOperation.PersonalActorId))]
    [Arguments(nameof(VisitorEnrollmentOperation.ExternalLoginId))]
    [Arguments(nameof(VisitorEnrollmentOperation.CredentialOperationId))]
    [Arguments(nameof(VisitorEnrollmentOperation.CredentialGeneration))]
    [Arguments(nameof(VisitorEnrollmentOperation.TenantUserId))]
    public async Task ActualGlobalLoginCannotReadPrivateEnrollmentEvidence(string property)
    {
        await using var migration = OpenMigrator();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(global);
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT {names.Column(typeof(VisitorEnrollmentOperation), property)} FROM {names.Table(typeof(VisitorEnrollmentOperation))} LIMIT 1";

        var denied = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());

        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task ActualGlobalLoginCannotMutateEnrollmentMetadata()
    {
        await using var migration = OpenMigrator();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(global);
        string generation = names.Column(typeof(VisitorEnrollmentOperation), nameof(VisitorEnrollmentOperation.Generation));
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"UPDATE {names.Table(typeof(VisitorEnrollmentOperation))} SET {generation}={generation}+1 WHERE false";

        var denied = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());

        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    [Arguments("missing-column")]
    [Arguments("private-column")]
    [Arguments("grant-option")]
    [Arguments("table-read")]
    [Arguments("unknown-policy")]
    [Arguments("changed-policy")]
    [Arguments("missing-policy")]
    [Arguments("disabled-rls")]
    public async Task PartialOrUnknownProfileIsDeniedAndNeverRepairedAtRuntime(string drift)
    {
        await using var migration = OpenMigrator();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await migration.Database.OpenConnectionAsync();
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(VisitorEnrollmentOperation));
        string login = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string column = names.Column(typeof(VisitorEnrollmentOperation), nameof(VisitorEnrollmentOperation.Generation));
        string digest = names.Column(typeof(VisitorEnrollmentOperation), nameof(VisitorEnrollmentOperation.IntentDigest));
        string policy = names.Quote("visitor_enrollment_recovery_metadata_global_read");
        string unknown = names.Quote("visitor_recovery_test_unknown_policy");
        var fault = drift switch
        {
            "missing-column" => ($"REVOKE SELECT ({column}) ON {table} FROM {login}",
                $"GRANT SELECT ({column}) ON {table} TO {login}"),
            "private-column" => ($"GRANT SELECT ({digest}) ON {table} TO {login}",
                $"REVOKE SELECT ({digest}) ON {table} FROM {login}"),
            "grant-option" => ($"GRANT SELECT ({column}) ON {table} TO {login} WITH GRANT OPTION",
                $"REVOKE GRANT OPTION FOR SELECT ({column}) ON {table} FROM {login}"),
            "table-read" => ($"GRANT SELECT ON {table} TO {login}", $"REVOKE SELECT ON {table} FROM {login}"),
            "unknown-policy" => ($"CREATE POLICY {unknown} ON {table} FOR SELECT TO {login} USING (true)",
                $"DROP POLICY {unknown} ON {table}"),
            "changed-policy" => ($"ALTER POLICY {policy} ON {table} USING (false)",
                $"ALTER POLICY {policy} ON {table} USING (true)"),
            "missing-policy" => ($"DROP POLICY {policy} ON {table}",
                $"CREATE POLICY {policy} ON {table} FOR SELECT TO {login} USING (true)"),
            "disabled-rls" => ($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY",
                $"ALTER TABLE {table} FORCE ROW LEVEL SECURITY"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migration.Database.ExecuteSqlRawAsync(fault.Item1);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment));
            string before = await CatalogueImageAsync(migration);
            var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(rejected.Message).IsEqualTo("visitor_enrollment_recovery_read_permissions_drift");
            await Assert.That(await CatalogueImageAsync(migration)).IsEqualTo(before);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(fault.Item2);
            // REVOKE at table level may remove column reads as well.
            if (drift == "table-read")
            {
                string[] properties = [nameof(VisitorEnrollmentOperation.Id), nameof(VisitorEnrollmentOperation.TenantId),
                    nameof(VisitorEnrollmentOperation.Stage), nameof(VisitorEnrollmentOperation.Generation),
                    nameof(VisitorEnrollmentOperation.ExpiresAt), nameof(VisitorEnrollmentOperation.CleanupCompletedAt)];
                string columns = string.Join(',', properties.Select(property => names.Column(typeof(VisitorEnrollmentOperation), property)));
                await migration.Database.ExecuteSqlRawAsync($"GRANT SELECT ({columns}) ON {table} TO {login}");
            }
        }
    }

    [Test]
    public async Task ReapplicationRetainsTheOriginalMigratorTransactionAndConnector()
    {
        await using var migration = OpenMigrator();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await migration.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migration.Database.BeginTransactionAsync();
            var connector = RelationalConnectionIdentity.Capture(migration);
            string before = await CatalogueImageAsync(migration);
            await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
            await Assert.That(migration.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(connector.Matches(migration)).IsTrue();
            await Assert.That(connector.Owns(transaction.GetDbTransaction())).IsTrue();
            await Assert.That(await CatalogueImageAsync(migration)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task EntirelyAbsentProfileInstallsAtomicallyWithinTheMigratorTransaction()
    {
        await using var migration = OpenMigrator();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await migration.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migration.Database.BeginTransactionAsync();
            var connector = RelationalConnectionIdentity.Capture(migration);
            var names = new HumanPrincipalSourceSql(migration);
            string table = names.Table(typeof(VisitorEnrollmentOperation));
            string login = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
            string[] properties = [nameof(VisitorEnrollmentOperation.Id), nameof(VisitorEnrollmentOperation.TenantId),
                nameof(VisitorEnrollmentOperation.Stage), nameof(VisitorEnrollmentOperation.Generation),
                nameof(VisitorEnrollmentOperation.ExpiresAt), nameof(VisitorEnrollmentOperation.CleanupCompletedAt)];
            string columns = string.Join(',', properties.Select(property => names.Column(typeof(VisitorEnrollmentOperation), property)));
            string policy = names.Quote("visitor_enrollment_recovery_metadata_global_read");
            await migration.Database.ExecuteSqlRawAsync($"DROP POLICY {policy} ON {table}; REVOKE SELECT ({columns}) ON {table} FROM {login}");

            await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);

            await using var command = migration.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                SELECT (SELECT count(*)=6 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum>0 AND NOT a.attisdropped
                        AND pg_catalog.has_column_privilege(@login,a.attrelid,a.attnum,'SELECT'))
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid=pg_catalog.to_regclass(@table)
                            AND p.polname='visitor_enrollment_recovery_metadata_global_read')
                """;
            command.Parameters.Add(new NpgsqlParameter("table", table));
            command.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
            await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
            await Assert.That(connector.Matches(migration)).IsTrue();
            await Assert.That(migration.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task MissingEnrollmentRuntimeAndTenantSelectedOwnersAreRejected()
    {
        await using var migration = OpenMigrator();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(global, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(global, fixture.NativeQueueEnrollment));
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(runtime, fixture.NativeQueueEnrollment));
        await using var selected = await ((IMixedSettingsDbContextFactory)GlobalFactory())
            .CreateMixedSettingsAsync(Guid.CreateVersion7(), default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(selected, fixture.NativeQueueEnrollment));
    }

    [Test]
    public async Task GlobalTenantGucContaminationCannotPassReadiness()
    {
        await using var migration = OpenMigrator();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_catalog.set_config('app.current_tenant_id',@tenant,false)";
        command.Parameters.Add(new NpgsqlParameter("tenant", Guid.CreateVersion7().ToString()));
        await command.ExecuteScalarAsync();
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresVisitorEnrollmentRecoveryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment));
        }
        finally
        {
            command.Parameters[0].Value = "";
            await command.ExecuteScalarAsync();
        }
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private GlobalExploreDbContextFactory GlobalFactory() =>
        new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));

    private static async Task<string> CatalogueImageAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT jsonb_build_object(
                'relation',(SELECT to_jsonb(t) FROM pg_catalog.pg_class t WHERE t.oid=pg_catalog.to_regclass(@table)),
                'columns',(SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attnum) FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid=pg_catalog.to_regclass(@table)),
                'policies',(SELECT jsonb_agg(to_jsonb(p) ORDER BY p.oid) FROM pg_catalog.pg_policy p
                    WHERE p.polrelid=pg_catalog.to_regclass(@table)))::text
            """;
        command.Parameters.Add(new NpgsqlParameter("table", names.Table(typeof(VisitorEnrollmentOperation))));
        return (string)(await command.ExecuteScalarAsync())!;
    }
}

/// <summary>Portable metadata projection, stage eligibility, hard bounds and original-owner fences.</summary>
public sealed class VisitorEnrollmentRecoveryReaderSqliteTests
{
    [Test]
    public async Task SqliteProjectsEligibleStagesAcrossTenantsInDeadlineOrder()
    {
        await using var database = await VisitorRecoverySqliteDatabase.CreateAsync();
        await using var seed = EmailDispatchSqliteFixture.CreateContext(database.Path);
        seed.BindTenantLease();
        seed.EnableTenantFilterBypass("Visitor recovery test seeding.");
        var expected = await VisitorRecoveryRows.SeedStagesAsync(seed);

        var page = await new VisitorEnrollmentRecoveryReader(database, null).ReadCandidatesAsync(VisitorRecoveryRows.Now, 100);

        await Assert.That(page.Length).IsEqualTo(6);
        for (int index = 0; index < expected.Length; index++)
            await Assert.That(page[index]).IsEqualTo(expected[index]);
    }

    [Test]
    public async Task EqualDeadlinesUseStableIdsAndNeverReturnMoreThanOneHundred()
    {
        await using var database = await VisitorRecoverySqliteDatabase.CreateAsync();
        await using var seed = EmailDispatchSqliteFixture.CreateContext(database.Path);
        seed.BindTenantLease();
        seed.EnableTenantFilterBypass("Visitor recovery test seeding.");
        var tenant = VisitorRecoveryRows.Tenant();
        seed.Add(tenant);
        for (int index = 105; index >= 1; index--)
            seed.Add(VisitorRecoveryRows.Reserved(Guid.Parse($"00000000-0000-7000-8000-{index:x12}"),
                tenant.Id, VisitorRecoveryRows.Now.AddMinutes(-15)));
        await seed.SaveChangesAsync();
        var reader = new VisitorEnrollmentRecoveryReader(database, null);

        var page = await reader.ReadCandidatesAsync(VisitorRecoveryRows.Now, 100);
        var repeated = await reader.ReadCandidatesAsync(VisitorRecoveryRows.Now, 1);

        await Assert.That(page.Length).IsEqualTo(100);
        for (int index = 0; index < 100; index++)
            await Assert.That(page[index].Id).IsEqualTo(Guid.Parse($"00000000-0000-7000-8000-{index + 1:x12}"));
        await Assert.That(repeated.Length).IsEqualTo(1);
        await Assert.That(repeated[0]).IsEqualTo(page[0]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(101)]
    public async Task OutOfContractBoundsAreRejected(int maximum)
    {
        await using var database = await VisitorRecoverySqliteDatabase.CreateAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await new VisitorEnrollmentRecoveryReader(database, null).ReadCandidatesAsync(VisitorRecoveryRows.Now, maximum));
    }

    [Test]
    public async Task UnspecifiedDeadlinesAndCanceledCallsAreRejected()
    {
        await using var database = await VisitorRecoverySqliteDatabase.CreateAsync();
        var reader = new VisitorEnrollmentRecoveryReader(database, null);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await reader.ReadCandidatesAsync(DateTime.SpecifyKind(VisitorRecoveryRows.Now, DateTimeKind.Unspecified), 1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await reader.ReadCandidatesAsync(VisitorRecoveryRows.Now, 1, canceled.Token));
    }

    [Test]
    public async Task TenantSelectedLeaseCannotBeUsedForCrossTenantDiscovery()
    {
        await using var database = await VisitorRecoverySqliteDatabase.CreateAsync();
        database.TenantId = Guid.CreateVersion7();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await new VisitorEnrollmentRecoveryReader(database, null).ReadCandidatesAsync(VisitorRecoveryRows.Now, 1));
    }

    [Test]
    public async Task OwnerChangeDuringProjectionCannotReleaseMetadata()
    {
        await using var database = await VisitorRecoverySqliteDatabase.CreateAsync();
        database.Interceptor = new ChangeVisitorRecoveryOwner();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new VisitorEnrollmentRecoveryReader(database, null).ReadCandidatesAsync(VisitorRecoveryRows.Now, 1));
    }

    private sealed class ChangeVisitorRecoveryOwner : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ((ExploreDbContext)eventData.Context!).TenantContext = new VisitorRecoveryTenantIdentity(Guid.CreateVersion7());
            return ValueTask.FromResult(result);
        }
    }
}

internal static class VisitorRecoveryRows
{
    internal static readonly DateTime Now = new(2044, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    internal static Tenant Tenant() => new()
    {
        Id = Guid.CreateVersion7(), FullName = "Visitor recovery fixture",
        Slug = $"visitor-recovery-{Guid.CreateVersion7():N}",
        TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
    };

    internal static VisitorEnrollmentOperation Reserved(Guid id, Guid tenantId, DateTime createdAt) =>
        VisitorEnrollmentOperation.Reserve(id, tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), createdAt);

    internal static async Task<ImmutableArray<VisitorEnrollmentRecoveryCandidate>> SeedStagesAsync(ExploreDbContext context)
    {
        var first = Tenant();
        var second = Tenant();
        context.AddRange(first, second);
        var reserved = Reserved(Guid.CreateVersion7(), first.Id, Now.AddMinutes(-15));
        var prepared = Reserved(Guid.CreateVersion7(), second.Id, Now.AddMinutes(-16));
        prepared.RecordIdentityPrepared(1, prepared.CredentialOperationId, 1, prepared.CreatedAt.AddMinutes(1));
        var admitted = Reserved(Guid.CreateVersion7(), first.Id, Now.AddMinutes(-5));
        admitted.RecordIdentityPrepared(1, admitted.CredentialOperationId, 1, admitted.CreatedAt.AddMinutes(1));
        admitted.RecordPrimaryAdmission(2, admitted.CredentialOperationId, Guid.CreateVersion7(), admitted.CreatedAt.AddMinutes(2));
        var canceled = Reserved(Guid.CreateVersion7(), second.Id, Now.AddMinutes(-4));
        canceled.Cancel(1, canceled.CredentialOperationId, canceled.CreatedAt.AddMinutes(1));
        var expiredAdmitted = Reserved(Guid.CreateVersion7(), second.Id, Now.AddMinutes(-20));
        expiredAdmitted.RecordIdentityPrepared(1, expiredAdmitted.CredentialOperationId, 1, expiredAdmitted.CreatedAt.AddMinutes(1));
        expiredAdmitted.RecordPrimaryAdmission(2, expiredAdmitted.CredentialOperationId, Guid.CreateVersion7(), expiredAdmitted.CreatedAt.AddMinutes(2));
        var expiredCanceled = Reserved(Guid.CreateVersion7(), first.Id, Now.AddMinutes(-19));
        expiredCanceled.Cancel(1, expiredCanceled.CredentialOperationId, expiredCanceled.CreatedAt.AddMinutes(1));
        var active = Reserved(Guid.CreateVersion7(), first.Id, Now.AddMinutes(-18));
        active.RecordIdentityPrepared(1, active.CredentialOperationId, 1, active.CreatedAt.AddMinutes(1));
        active.RecordPrimaryAdmission(2, active.CredentialOperationId, Guid.CreateVersion7(), active.CreatedAt.AddMinutes(2));
        active.Activate(3, active.CredentialOperationId, active.CreatedAt.AddMinutes(3));
        var futureReserved = Reserved(Guid.CreateVersion7(), first.Id, Now.AddMinutes(-13));
        var futurePrepared = Reserved(Guid.CreateVersion7(), second.Id, Now.AddMinutes(-14));
        futurePrepared.RecordIdentityPrepared(1, futurePrepared.CredentialOperationId, 1, futurePrepared.CreatedAt.AddMinutes(1));
        var cleaned = Reserved(Guid.CreateVersion7(), first.Id, Now.AddMinutes(-30));
        cleaned.Cancel(1, cleaned.CredentialOperationId, cleaned.CreatedAt.AddMinutes(15));
        cleaned.AcknowledgeCancellationCleanup(2, cleaned.CredentialOperationId, cleaned.CreatedAt.AddMinutes(16));
        context.AddRange(reserved, prepared, admitted, canceled, expiredAdmitted, expiredCanceled,
            active, futureReserved, futurePrepared, cleaned);
        await context.SaveChangesAsync();
        return
        [
            new(expiredAdmitted.Id, second.Id, VisitorEnrollmentStage.PrimaryAdmitted, 3, Now.AddMinutes(-5)),
            new(expiredCanceled.Id, first.Id, VisitorEnrollmentStage.Canceled, 2, Now.AddMinutes(-4)),
            new(prepared.Id, second.Id, VisitorEnrollmentStage.IdentityPrepared, 2, Now.AddMinutes(-1)),
            new(reserved.Id, first.Id, VisitorEnrollmentStage.Reserved, 1, Now),
            new(admitted.Id, first.Id, VisitorEnrollmentStage.PrimaryAdmitted, 3, Now.AddMinutes(10)),
            new(canceled.Id, second.Id, VisitorEnrollmentStage.Canceled, 2, Now.AddMinutes(11))
        ];
    }
}

internal sealed class VisitorRecoverySqliteDatabase(string path) : IGlobalExploreDbContextFactory, IAsyncDisposable
{
    internal string Path { get; } = path;
    internal Guid? TenantId { get; set; }
    internal IInterceptor? Interceptor { get; set; }

    internal static async Task<VisitorRecoverySqliteDatabase> CreateAsync()
    {
        var database = new VisitorRecoverySqliteDatabase(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"visitor-recovery-{Guid.CreateVersion7():N}.db"));
        await EmailDispatchSqliteFixture.CreateDatabaseAsync(database.Path);
        return database;
    }

    public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Interceptor is { } interceptor
            ? EmailDispatchSqliteFixture.CreateContext(Path, interceptor)
            : EmailDispatchSqliteFixture.CreateContext(Path);
        context.TenantContext = TenantId is { } tenant ? new VisitorRecoveryTenantIdentity(tenant) : null;
        context.BindTenantLease();
        return Task.FromResult(context);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(Path);
        return ValueTask.CompletedTask;
    }
}

internal sealed record VisitorRecoveryTenantIdentity(Guid TenantId) : ITenantContext;
