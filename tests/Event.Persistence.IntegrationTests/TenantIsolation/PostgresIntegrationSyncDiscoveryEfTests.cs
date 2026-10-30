using System.Data;
using System.Diagnostics;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

/// <summary>Queue discovery exports identities, never subscriber content or claim authority.</summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresIntegrationSyncDiscoveryEfTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2001, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StaleBefore = Now.AddMinutes(-5);
    private static readonly string[] Metadata =
    [
        nameof(IntegrationSyncOutbox.Id), nameof(IntegrationSyncOutbox.TenantId),
        nameof(IntegrationSyncOutbox.Status), nameof(IntegrationSyncOutbox.NextAttemptAt),
        nameof(IntegrationSyncOutbox.ProcessingStartedAt), nameof(IntegrationSyncOutbox.CreatedAt),
        nameof(IntegrationSyncOutbox.IsDeleted)
    ];

    [Test]
    public async Task ConfiguredGlobalEfProjectionPreservesExactEligibilityAndOrder()
    {
        var expected = await SeedEligibilityAsync();
        await using var global = await GlobalFactory().CreateDbContextAsync();
        var actual = await MetadataPage(global, Now, StaleBefore, 1000).ToArrayAsync();
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (int index = 0; index < expected.Length; index++)
            await Assert.That(actual[index]).IsEqualTo(expected[index]);
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    public async Task ConfiguredGlobalHasExactlySevenDirectOwnerIssuedNongrantableColumns()
    {
        await using var migration = OpenMigrator();
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(IntegrationSyncOutbox));
        string[] columns = Metadata.Select(property => names.ColumnName(typeof(IntegrationSyncOutbox), property)).ToArray();
        await using var command = migration.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COALESCE(string_agg(a.attname,'|' ORDER BY a.attname),'')
            FROM pg_catalog.pg_attribute a
            WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                AND pg_catalog.has_column_privilege(@login,a.attrelid,a.attnum,'SELECT')
            """;
        command.Parameters.Add(new NpgsqlParameter("table", table));
        command.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(string.Join('|', columns.Order(StringComparer.Ordinal)));
        command.CommandText = """
            SELECT NOT pg_catalog.has_table_privilege(@login,pg_catalog.to_regclass(@table),
                'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                AND NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class t ON t.oid=a.attrelid
                    WHERE t.oid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                        AND (pg_catalog.has_column_privilege(@login,t.oid,a.attnum,
                            'SELECT WITH GRANT OPTION,INSERT,UPDATE,REFERENCES')
                            OR (pg_catalog.has_column_privilege(@login,t.oid,a.attnum,'SELECT')
                                AND (a.attnum<0
                                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                        WHERE acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@login)
                                            AND acl.grantor=t.relowner AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                                    OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                        WHERE acl.grantee=0 AND acl.privilege_type='SELECT')))))
            """;
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    [Test]
    [Arguments(nameof(IntegrationSyncOutbox.SubscriberEmail))]
    [Arguments(nameof(IntegrationSyncOutbox.SubscriberPayloadJson))]
    [Arguments(nameof(IntegrationSyncOutbox.UserId))]
    [Arguments(nameof(IntegrationSyncOutbox.ProcessingLeaseToken))]
    public async Task MetadataDiscoveryCannotReadPrivateSubscriberOrClaimFields(string property)
    {
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(global);
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT {names.Column(typeof(IntegrationSyncOutbox), property)} FROM {names.Table(typeof(IntegrationSyncOutbox))} WHERE false";
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task DiscoveryInstallationContainsNoRetiredBusinessRoutineOrCapabilityRoles()
    {
        await using var migration = OpenMigrator();
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        await using var command = migration.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname=@schema AND p.proname=@routine)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                    ('event_integration_sync_discovery_owner','event_integration_sync_discovery_migrator',
                        'event_integration_sync_discovery_runtime'))
            """;
        command.Parameters.Add(new NpgsqlParameter("schema", names.Schema));
        command.Parameters.Add(new NpgsqlParameter("routine",
            migration.Model.FindEntityType(typeof(IntegrationSyncOutbox))!.GetTableName()! + "_pending_owners"));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    [Test]
    public async Task OriginalGlobalReaderKeepsInclusiveCutoffsIndependentStatusesAndBoundedOrder()
    {
        var expected = await SeedEligibilityAsync();
        var discovery = new IntegrationSyncQueueDiscovery(GlobalFactory(), fixture.NativeQueueEnrollment);
        var actual = await discovery.GetPendingAsync(1000, Now, StaleBefore, CancellationToken.None);
        await Assert.That(actual.Count).IsEqualTo(expected.Length);
        for (int index = 0; index < expected.Length; index++)
            await Assert.That(actual[index]).IsEqualTo(expected[index]);
        var first = await discovery.GetPendingAsync(1, Now, StaleBefore, CancellationToken.None);
        await Assert.That(first.Count).IsEqualTo(1);
        await Assert.That(first[0]).IsEqualTo(expected[0]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            discovery.GetPendingAsync(0, Now, StaleBefore, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            discovery.GetPendingAsync(1001, Now, StaleBefore, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            discovery.GetPendingAsync(1, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), StaleBefore, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            discovery.GetPendingAsync(1, Now, Now.AddSeconds(1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            discovery.GetPendingAsync(1, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), StaleBefore, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            discovery.GetPendingAsync(1, Now, DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc), CancellationToken.None));
    }

    [Test]
    public async Task FutureCallerTimeCannotMakeFutureRetryOrProcessingWorkEligible()
    {
        await fixture.ResetAsync();
        var tenant = CreateTenant();
        DateTime observed = DateTime.UtcNow;
        var ready = Row(tenant.Id, IntegrationSyncStatus.Pending, 0);
        var retry = Row(tenant.Id, IntegrationSyncStatus.RetryScheduled, 1);
        retry.NextAttemptAt = observed.AddDays(2);
        var processing = Row(tenant.Id, IntegrationSyncStatus.Processing, 2);
        processing.ProcessingStartedAt = observed.AddDays(2);
        await SeedAsync([tenant], [ready, retry, processing]);
        var discovery = new IntegrationSyncQueueDiscovery(GlobalFactory(), fixture.NativeQueueEnrollment);
        var actual = await discovery.GetPendingAsync(1000, observed.AddDays(3), observed.AddDays(3), CancellationToken.None);
        await Assert.That(actual).IsEquivalentTo([new IntegrationSyncQueueCandidate(tenant.Id, ready.Id)]);
    }

    [Test]
    [Arguments("table")]
    [Arguments("private")]
    [Arguments("write")]
    [Arguments("public")]
    [Arguments("grant-option")]
    [Arguments("system-column")]
    [Arguments("admin-only")]
    public async Task DiscoveryRefusesPermissionDriftWithoutRepair(string change)
    {
        await using var migrator = OpenMigrator();
        await PostgresIntegrationSyncReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync();
            var names = new HumanPrincipalSourceSql(migrator);
            string table = names.Table(typeof(IntegrationSyncOutbox));
            string id = names.Column(typeof(IntegrationSyncOutbox), nameof(IntegrationSyncOutbox.Id));
            string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
            string other = names.Quote("integration_drift_" + Guid.CreateVersion7().ToString("N"));
            string sql = change switch
            {
                "table" => $"GRANT SELECT ON {table} TO {global}",
                "private" => $"GRANT SELECT ({names.Column(typeof(IntegrationSyncOutbox), nameof(IntegrationSyncOutbox.SubscriberEmail))}) ON {table} TO {global}",
                "write" => $"GRANT UPDATE ({names.Column(typeof(IntegrationSyncOutbox), nameof(IntegrationSyncOutbox.Status))}) ON {table} TO {global}",
                "public" => $"GRANT SELECT ({id}) ON {table} TO PUBLIC",
                "grant-option" => $"GRANT SELECT ({id}) ON {table} TO {global} WITH GRANT OPTION",
                "system-column" => $"GRANT SELECT (xmin) ON {table} TO {global}",
                "admin-only" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({id}) ON {table} TO {other}; GRANT {other} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            await migrator.Database.ExecuteSqlRawAsync(sql);
            string before = await CatalogueImageAsync(migrator);
            await transaction.CreateSavepointAsync("original_global");
            await migrator.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {global}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresIntegrationSyncReadPermissions.RequireReadyAsync(migrator, fixture.NativeQueueEnrollment));
            await transaction.RollbackToSavepointAsync("original_global");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresIntegrationSyncReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment));
            await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(before);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresIntegrationSyncReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment));
            await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task ReadinessRequiresConfiguredNullGlobalAndOriginalEnrollment()
    {
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresIntegrationSyncReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresIntegrationSyncReadPermissions.RequireReadyAsync(global, null));
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresIntegrationSyncReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment));
        await using var selected = await ((IMixedSettingsDbContextFactory)GlobalFactory())
            .CreateMixedSettingsAsync(Guid.CreateVersion7(), default);
        await selected.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresIntegrationSyncReadPermissions.RequireReadyAsync(selected, fixture.NativeQueueEnrollment));
    }

    [Test]
    public async Task CompleteProfileReapplicationPreservesCallerTransactionAndCatalogue()
    {
        await using var migrator = OpenMigrator();
        await PostgresIntegrationSyncReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync();
            var connection = RelationalConnectionIdentity.Capture(migrator);
            string before = await CatalogueImageAsync(migrator);
            await PostgresIntegrationSyncReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
            await PostgresIntegrationSyncReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(connection.Matches(migrator)).IsTrue();
            await Assert.That(connection.Owns(transaction.GetDbTransaction())).IsTrue();
            await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task ConfiguredRetryCannotReanchorDiscoveryToAReplacementBackend()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        var expected = await SeedEligibilityAsync();
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await using (var configuration = await factory.CreateDbContextAsync(token))
            await Assert.That(configuration.Database.CreateExecutionStrategy().RetriesOnFailure).IsTrue();
        var discovery = new IntegrationSyncQueueDiscovery(factory, fixture.NativeQueueEnrollment);
        using var fault = new DiscoveryRetryFault();
        var healthy = await discovery.GetPendingAsync(1000, Now, StaleBefore, token);
        await Assert.That(healthy).IsEquivalentTo(expected);
        await Assert.That(fault.WarmReads).IsGreaterThanOrEqualTo(2);

        fault.Arm();
        IReadOnlyList<IntegrationSyncQueueCandidate>? escaped = null;
        Exception? failure = null;
        try
        {
            escaped = await discovery.GetPendingAsync(1000, Now, StaleBefore, token);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        await Assert.That(fault.Faults).IsEqualTo(1);
        await Assert.That(fault.OriginalBackend).IsGreaterThan(0);
        await Assert.That(fault.FaultWasTransient).IsTrue();
        await Assert.That(fault.ClosedOriginal).IsTrue();
        if (escaped is not null)
            await Assert.That(fault.ReplacementBackendObserved).IsTrue()
                .Because("an escaped page must be attributed to an actual replacement-backend retry, not a fabricated result");
        await Assert.That(failure is UnauthorizedAccessException).IsTrue()
            .Because($"a transient provider retry cannot replace the original discovery owner and return a page; failure_type={failure?.GetType().FullName ?? "<none>"}; escaped_page={escaped is not null}; replacement_observed={fault.ReplacementBackendObserved}");
        await Assert.That(escaped).IsNull();
    }

    private sealed class DiscoveryRetryFault : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable listeners;
        private readonly System.Collections.Concurrent.ConcurrentBag<IDisposable> subscriptions = [];
        private ExploreDbContext? originalContext;
        private bool armed;
        public int WarmReads { get; private set; }
        public int Faults { get; private set; }
        public int OriginalBackend { get; private set; }
        public bool FaultWasTransient { get; private set; }
        public bool ClosedOriginal { get; private set; }
        public bool ReplacementBackendObserved { get; private set; }

        public DiscoveryRetryFault() => listeners = DiagnosticListener.AllListeners.Subscribe(this);

        public void Arm()
        {
            if (originalContext is null || WarmReads < 2)
                throw new InvalidOperationException("A real successful discovery must precede the connection fault.");
            armed = true;
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
                subscriptions.Add(listener.Subscribe(this, name => name == RelationalEventId.CommandExecuted.Name));
        }

        public void OnNext(KeyValuePair<string, object?> diagnostic)
        {
            if (diagnostic.Value is not CommandExecutedEventData command
                || command.ExecuteMethod != DbCommandMethod.ExecuteReader
                || command.Context is not ExploreDbContext context
                || command.Command.Connection is not NpgsqlConnection connection)
                return;
            if (!armed)
            {
                originalContext ??= context;
                if (ReferenceEquals(context, originalContext)) WarmReads++;
                return;
            }
            if (!ReferenceEquals(context, originalContext)) return;
            if (Faults != 0)
            {
                ReplacementBackendObserved |= connection.ProcessID != OriginalBackend;
                return;
            }
            context.RequireBoundTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || connection.State != ConnectionState.Open
                || !ReferenceEquals(context.Database.GetDbConnection(), connection)
                || context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("The fault must target the actual null-bound Global discovery command.");
            OriginalBackend = connection.ProcessID;
            Faults++;
            NpgsqlConnection.ClearPool(connection);
            connection.Close();
            ClosedOriginal = connection.State == ConnectionState.Closed;
            var transient = new NpgsqlException("Controlled discovery connection interruption.",
                new System.IO.IOException("The test closed the original discovery connector after its real command."));
            FaultWasTransient = transient.IsTransient;
            throw transient;
        }

        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
        public void Dispose()
        {
            listeners.Dispose();
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }

    private static async Task<string> CatalogueImageAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT jsonb_build_object(
                'relation',(SELECT to_jsonb(c) FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass(@table)),
                'columns',(SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attnum) FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid=pg_catalog.to_regclass(@table)),
                'policies',(SELECT jsonb_agg(to_jsonb(p) ORDER BY p.oid) FROM pg_catalog.pg_policy p
                    WHERE p.polrelid=pg_catalog.to_regclass(@table)),
                'memberships',(SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member,m.grantor)
                    FROM pg_catalog.pg_auth_members m))::text
            """;
        command.Parameters.Add(new NpgsqlParameter("table", names.Table(typeof(IntegrationSyncOutbox))));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<IntegrationSyncQueueCandidate[]> SeedEligibilityAsync()
    {
        await fixture.ResetAsync();
        var first = CreateTenant();
        var second = CreateTenant();
        var pending = Row(first.Id, IntegrationSyncStatus.Pending, 0);
        var retry = Row(second.Id, IntegrationSyncStatus.RetryScheduled, 1);
        retry.NextAttemptAt = Now;
        var missingStart = Row(first.Id, IntegrationSyncStatus.Processing, 2);
        var stale = Row(second.Id, IntegrationSyncStatus.Processing, 3);
        stale.ProcessingStartedAt = StaleBefore;
        var future = Row(first.Id, IntegrationSyncStatus.Pending, 4);
        future.NextAttemptAt = Now.AddMilliseconds(1);
        var fresh = Row(second.Id, IntegrationSyncStatus.Processing, 5);
        fresh.ProcessingStartedAt = StaleBefore.AddMilliseconds(1);
        var completed = Row(first.Id, IntegrationSyncStatus.Completed, 6);
        var deleted = Row(second.Id, IntegrationSyncStatus.Pending, 7);
        deleted.IsDeleted = true;
        deleted.DeletedAt = Now;
        await SeedAsync([first, second], [pending, retry, missingStart, stale, future, fresh, completed, deleted]);
        return [new(first.Id, pending.Id), new(second.Id, retry.Id), new(first.Id, missingStart.Id), new(second.Id, stale.Id)];
    }

    private async Task SeedAsync(Tenant[] tenants, IntegrationSyncOutbox[] rows)
    {
        await using var migration = OpenMigrator();
        await using var humanGate = new HumanSecuritySourceGate(migration);
        await using var human = await humanGate.AcquireAsync();
        await using var enrollmentGate = new EventDiscoveryEnrollmentGate(migration);
        await using var enrollment = await enrollmentGate.AcquireAsync();
        migration.Tenants.AddRange(tenants);
        migration.IntegrationSyncOutbox.AddRange(rows);
        await migration.SaveChangesAsync();
    }

    private static IQueryable<IntegrationSyncQueueCandidate> MetadataPage(
        ExploreDbContext context, DateTime now, DateTime staleBefore, int take) =>
        context.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.IntegrationSyncWorkerCrossTenantQueue)
            .IgnoreAutoIncludes().AsNoTracking()
            .Where(row => !row.IsDeleted &&
                (((row.Status == IntegrationSyncStatus.Pending || row.Status == IntegrationSyncStatus.RetryScheduled)
                    && (row.NextAttemptAt == null || row.NextAttemptAt <= now))
                    || (row.Status == IntegrationSyncStatus.Processing
                        && (row.ProcessingStartedAt == null || row.ProcessingStartedAt <= staleBefore))))
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Take(take)
            .Select(row => new IntegrationSyncQueueCandidate(row.TenantId, row.Id));

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private GlobalExploreDbContextFactory GlobalFactory() => new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));

    private static Tenant CreateTenant()
    {
        Guid id = Guid.CreateVersion7();
        return new Tenant
        {
            Id = id,
            FullName = "Integration discovery fixture",
            Slug = $"discovery-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
    }

    private static IntegrationSyncOutbox Row(Guid tenant, IntegrationSyncStatus status, int order) => new()
    {
        TenantId = tenant,
        Kind = IntegrationKind.Listmonk,
        SourceType = "integration-discovery-fixture",
        SourceId = Guid.CreateVersion7(),
        SubscriberEmail = "synthetic-discovery@example.invalid",
        SubscriberPayloadJson = "{}",
        ListmonkListId = 1,
        Status = status,
        CreatedAt = Now.AddHours(-1).AddSeconds(order)
    };
}
