using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed class AtprotoFederationSettingsReadTests
{
    private const string EventsEnabled = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
    private const string BackfillEnabled = GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled;
    private const string BackfillMode = GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode;

    [Test]
    public async Task SelectedTenantReadsDedicatedRowsWithoutAHumanWriterPurpose()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        await using var context = fixture.CreateContext(fixture.ActiveTenantId);
        var reader = new AtprotoFederationSettingRepository(context, null);

        var instance = await reader.GetInstanceAsync([EventsEnabled]);
        var tenant = await reader.GetForTenantAsync(fixture.ActiveTenantId, [EventsEnabled]);

        await Assert.That(instance.Single().Value).IsEqualTo("false");
        await Assert.That(tenant.Single().Value).IsEqualTo("true");
        await Assert.That(tenant.Single().TenantId).IsEqualTo(fixture.ActiveTenantId);
        await Assert.That(context.CurrentUserService).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    public async Task InstanceReadDoesNotRequireOrChangeTheSelectedTenant()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        foreach (Guid? selected in new Guid?[] { null, fixture.ActiveTenantId })
        {
            await using var context = fixture.CreateContext(selected);
            var rows = await new AtprotoFederationSettingRepository(context, null)
                .GetInstanceAsync([BackfillEnabled, BackfillMode]);
            await Assert.That(rows.Count).IsEqualTo(2);
            await Assert.That(rows.Single(row => row.SettingKey == BackfillMode).Value).IsEqualTo("\"downtime_only\"");
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(selected);
        }
    }

    [Test]
    public async Task ScopedReadRetainsOriginalTransactionAndItsUncommittedPolicy()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        await using var context = fixture.CreateContext(fixture.ActiveTenantId);
        await using var transaction = await context.Database.BeginTransactionAsync();
        DbConnection connection = context.Database.GetDbConnection();
        var changed = await context.Set<AtprotoFederationTenantSetting>()
            .SingleAsync(row => row.SettingKey == EventsEnabled);
        changed.SetValue("false", FederationReadDatabase.ObservedAt, null);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var observed = await new AtprotoFederationSettingRepository(context, null)
            .GetForTenantAsync(fixture.ActiveTenantId, [EventsEnabled]);

        await Assert.That(observed.Single().Value).IsEqualTo("false");
        await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, transaction)).IsTrue();
        await Assert.That(ReferenceEquals(context.Database.GetDbConnection(), connection)).IsTrue();
        await Assert.That(ReferenceEquals(transaction.GetDbTransaction().Connection, connection)).IsTrue();
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
        await transaction.RollbackAsync();

        await using var fresh = fixture.CreateContext(fixture.ActiveTenantId);
        var persisted = await new AtprotoFederationSettingRepository(fresh, null)
            .GetForTenantAsync(fixture.ActiveTenantId, [EventsEnabled]);
        await Assert.That(persisted.Single().Value).IsEqualTo("true");
    }

    [Test]
    public async Task ScopedReadClosesOnlyTheConnectionItOpened()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        await using var context = fixture.CreateContext(fixture.ActiveTenantId);
        var reader = new AtprotoFederationSettingRepository(context, null);
        DbConnection connection = context.Database.GetDbConnection();
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Closed);

        await reader.GetInstanceAsync([EventsEnabled]);
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Closed);

        await context.Database.OpenConnectionAsync();
        await reader.GetForTenantAsync(fixture.ActiveTenantId, [EventsEnabled]);
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(ReferenceEquals(context.Database.GetDbConnection(), connection)).IsTrue();
    }

    [Test]
    public async Task ForeignAndAbsentSelectedTenantsCannotBorrowScopedReads()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        foreach (Guid? selected in new Guid?[] { fixture.InactiveTenantId, null })
        {
            await using var context = fixture.CreateContext(selected);
            var reader = new AtprotoFederationSettingRepository(context, null);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                reader.GetForTenantAsync(fixture.ActiveTenantId, [EventsEnabled]));
        }
    }

    [Test]
    public async Task EmptyTenantIsRejectedEvenForAnEmptyKeySet()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        await using var context = fixture.CreateContext(fixture.ActiveTenantId);
        var reader = new AtprotoFederationSettingRepository(context, null);
        await Assert.ThrowsAsync<ArgumentException>(() => reader.GetForTenantAsync(Guid.Empty, []));
    }

    [Test]
    public async Task ScopedReadsRejectUnboundBypassedAndImportContexts()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        await using var unbound = fixture.CreateContext(fixture.ActiveTenantId, bind: false);
        await using var bypassed = fixture.CreateContext(fixture.ActiveTenantId);
        bypassed.EnableTenantFilterBypass("Federation read adversarial context");
        await using var import = fixture.CreateContext(null);
        import.BindConfigurationInstanceImportOwner();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AtprotoFederationSettingRepository(unbound, null).GetInstanceAsync([EventsEnabled]));
        foreach (ExploreDbContext context in new[] { bypassed, import })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                new AtprotoFederationSettingRepository(context, null).GetInstanceAsync([EventsEnabled]));
    }

    [Test]
    public async Task GlobalOverrideReadObservesBothKeysAcrossAllTenantStatesInOneQuery()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        var factory = new FederationReadFactory(fixture);
        fixture.Commands.Reset();
        var rows = await new AtprotoFederationGlobalReadRepository(factory, null)
            .GetOverridesAsync([BackfillEnabled, BackfillMode]);

        await Assert.That(rows.Count).IsEqualTo(4);
        await Assert.That(rows.Count(row => row.TenantId == fixture.InactiveTenantId)).IsEqualTo(2);
        await Assert.That(rows.Single(row => row.TenantId == fixture.ActiveTenantId && row.SettingKey == BackfillMode).Value)
            .IsEqualTo("\"FuLl\"");
        await Assert.That(rows.Any(row => row.SettingKey == EventsEnabled)).IsFalse();
        await Assert.That(fixture.Commands.ReaderCount).IsEqualTo(1);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => factory.LastContext!
            .Set<AtprotoFederationTenantSetting>().CountAsync());
    }

    [Test]
    public async Task GlobalReadRejectsTenantSelectedOrBypassedFactoryLeases()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        foreach (bool bypass in new[] { false, true })
        {
            var factory = new FederationReadFactory(fixture,
                selectedTenant: bypass ? null : fixture.ActiveTenantId, bypass: bypass);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                new AtprotoFederationGlobalReadRepository(factory, null).GetOverridesAsync([EventsEnabled]));
        }
    }

    [Test]
    [Arguments("private.unrelated_policy")]
    [Arguments(GovernanceSettingKeys.Federation.AtprotoEventValidationProfile)]
    [Arguments(GovernanceSettingKeys.Federation.AtprotoPublishMyEvents)]
    [Arguments("Federation.atproto_events_enabled")]
    public async Task ReadersRejectUnknownUnmovedAndNoncanonicalKeys(string key)
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        await using var context = fixture.CreateContext(fixture.ActiveTenantId);
        var scoped = new AtprotoFederationSettingRepository(context, null);
        var global = new AtprotoFederationGlobalReadRepository(new FederationReadFactory(fixture), null);
        await Assert.ThrowsAsync<ArgumentException>(() => scoped.GetInstanceAsync([key]));
        await Assert.ThrowsAsync<ArgumentException>(() => scoped.GetForTenantAsync(fixture.ActiveTenantId, [key]));
        await Assert.ThrowsAsync<ArgumentException>(() => global.GetInstanceAsync([key]));
        await Assert.ThrowsAsync<ArgumentException>(() => global.GetOverridesAsync([key]));
    }

    [Test]
    public async Task EmptyKeySetsReturnNoRowsOrDataQueries()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        await using var context = fixture.CreateContext(fixture.ActiveTenantId);
        var scoped = new AtprotoFederationSettingRepository(context, null);
        var global = new AtprotoFederationGlobalReadRepository(new FederationReadFactory(fixture), null);
        fixture.Commands.Reset();

        await Assert.That(await scoped.GetInstanceAsync([])).IsEmpty();
        await Assert.That(await scoped.GetForTenantAsync(fixture.ActiveTenantId, [])).IsEmpty();
        await Assert.That(await global.GetInstanceAsync([])).IsEmpty();
        await Assert.That(await global.GetOverridesAsync([])).IsEmpty();
        await Assert.That(fixture.Commands.ReaderCount).IsEqualTo(0);
    }

    [Test]
    public async Task GlobalReadSnapshotsRequestedKeysBeforeAcquiringItsFactoryLease()
    {
        await using var fixture = await FederationReadDatabase.CreateAsync();
        string[] keys = [EventsEnabled, EventsEnabled];
        var factory = new FederationReadFactory(fixture, onCreate: () => keys[0] = BackfillMode);

        var rows = await new AtprotoFederationGlobalReadRepository(factory, null).GetOverridesAsync(keys);

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows.All(row => row.SettingKey == EventsEnabled)).IsTrue();
    }
}

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class AtprotoFederationSettingsNativeReadTests(PostgreSqlContainerFixture fixture)
{
    private const string EventsEnabled = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task EnrolledReadersCanMaterializeDedicatedInstanceColumns(PrimaryDatabaseRole role)
    {
        await using var context = Open(role);
        var rows = await context.Set<AtprotoFederationInstanceSetting>().AsNoTracking()
            .Where(row => row.SettingKey == EventsEnabled).ToArrayAsync();
        await Assert.That(rows.Length).IsEqualTo(1);
        await Assert.That(rows[0].Value).IsEqualTo("false");
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    public async Task IndependentGlobalFactoryReadsDedicatedInstancePolicy()
    {
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var rows = await new AtprotoFederationGlobalReadRepository(factory, fixture.NativeQueueEnrollment)
            .GetInstanceAsync([EventsEnabled]);
        await Assert.That(rows.Single().Value).IsEqualTo("false");
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task ScopedInstanceReadRetainsTheSelectedTenantAndPhysicalTransactionWithoutHumanPurpose(PrimaryDatabaseRole role)
    {
        Guid tenantId = Guid.CreateVersion7();
        await using var context = Open(role, tenantId);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            int backend = connection.ProcessID;

            var rows = await new AtprotoFederationSettingRepository(context, fixture.NativeQueueEnrollment)
                .GetInstanceAsync([EventsEnabled]);

            await Assert.That(rows.Single().Value).IsEqualTo("false");
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(tenantId);
            await Assert.That(context.CurrentUserService).IsNull();
            await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(ReferenceEquals(context.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task GlobalReadsInactiveOverridesWhileRuntimeRlsAndGlobalNullLeaseStillPreventCrossTenantMutation()
    {
        Guid active = Guid.CreateVersion7();
        Guid inactive = Guid.CreateVersion7();
        await using (var seed = Open(PrimaryDatabaseRole.Migrator))
        {
            foreach (var (id, status) in new[] { (active, TenantStatusEnum.Active), (inactive, TenantStatusEnum.Suspended) })
                seed.Add(new Tenant
                {
                    Id = id,
                    FullName = "Federation native read tenant",
                    Slug = $"federation-native-{id:N}",
                    TenantStatusId = (int)status,
                    TenantStatus = null!,
                    CreatedAt = FederationReadDatabase.ObservedAt
                });
            await seed.SaveChangesAsync();
        }
        string[] keys =
        [
            GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled,
            GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode
        ];
        foreach (Guid tenantId in new[] { active, inactive })
        {
            await using var seed = Open(PrimaryDatabaseRole.Migrator, tenantId);
            seed.Add(AtprotoFederationTenantSetting.Create(tenantId, keys[0], "true", false, FederationReadDatabase.ObservedAt, null));
            seed.Add(AtprotoFederationTenantSetting.Create(tenantId, keys[1], "\"full\"", false, FederationReadDatabase.ObservedAt, null));
            await seed.SaveChangesAsync();
        }

        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var rows = await new AtprotoFederationGlobalReadRepository(factory, fixture.NativeQueueEnrollment).GetOverridesAsync(keys);
        await Assert.That(rows.Count(row => row.TenantId == active || row.TenantId == inactive)).IsEqualTo(4);

        await using var runtime = Open(PrimaryDatabaseRole.Runtime, active);
        var runtimeRows = await runtime.Set<AtprotoFederationTenantSetting>()
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGovernanceResolution)
            .Where(row => row.TenantId == active || row.TenantId == inactive).AsNoTracking().ToArrayAsync();
        await Assert.That(runtimeRows.Length).IsEqualTo(2);
        await Assert.That(runtimeRows.All(row => row.TenantId == active)).IsTrue();

        await using var global = await factory.CreateDbContextAsync();
        int changed = await global.Set<AtprotoFederationTenantSetting>()
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGovernanceResolution)
            .Where(row => row.TenantId == inactive && row.SettingKey == keys[0])
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "false"));
        await Assert.That(changed).IsEqualTo(0);
        var retained = await new AtprotoFederationGlobalReadRepository(factory, fixture.NativeQueueEnrollment).GetOverridesAsync(keys);
        await Assert.That(retained.Single(row => row.TenantId == inactive && row.SettingKey == keys[0]).Value).IsEqualTo("true");
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task BothPhysicalOwnersRejectAMismatchedScopedTenant(PrimaryDatabaseRole role)
    {
        Guid selected = Guid.CreateVersion7();
        await using var context = Open(role, selected);
        var reader = new AtprotoFederationSettingRepository(context, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            reader.GetForTenantAsync(Guid.CreateVersion7(), [EventsEnabled]));
    }

    [Test]
    public async Task GlobalApiRejectsRuntimeEvenThoughRuntimeHasDeclaredFederationReadColumns()
    {
        var factory = new NativeReadFactory(() => Open(PrimaryDatabaseRole.Runtime));
        var reader = new AtprotoFederationGlobalReadRepository(factory, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetOverridesAsync([EventsEnabled]));
    }

    [Test]
    public async Task PostgreSqlReadRequiresExplicitEnrollment()
    {
        await using var context = Open(PrimaryDatabaseRole.Runtime);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new AtprotoFederationSettingRepository(context, null).GetInstanceAsync([EventsEnabled]));
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    public async Task OtherEnrolledServiceAccountsCannotReadDedicatedFederationPolicy(PrimaryDatabaseRole role)
    {
        await using var context = Open(role);
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Set<AtprotoFederationInstanceSetting>().AsNoTracking().AnyAsync());
        await Assert.That(failure.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task RuntimeCannotMutateTheInstanceFederationCeiling()
    {
        await using var context = Open(PrimaryDatabaseRole.Runtime);
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Set<AtprotoFederationInstanceSetting>().Where(row => row.Id == Guid.Empty)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "true")));
        await Assert.That(failure.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task FederationRightsDoNotExposePrivateGenericOrTenantSources(PrimaryDatabaseRole role)
    {
        await using var context = Open(role);
        var names = new HumanPrincipalSourceSql(context);
        foreach (Type type in new[] { typeof(SystemSetting), typeof(TenantSetting), typeof(UserPii), typeof(Tenant) })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {names.Table(type)} LIMIT 1"));
            await Assert.That(failure.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    [Arguments("table-read")]
    [Arguments("grant-option")]
    [Arguments("system-column")]
    [Arguments("required-column-missing")]
    [Arguments("public-read")]
    [Arguments("foreign-role-read")]
    [Arguments("set-only-read")]
    [Arguments("admin-only-read")]
    [Arguments("nested-admin-read")]
    [Arguments("schema-create")]
    [Arguments("undeclared-update-column")]
    [Arguments("runtime-instance-write")]
    public async Task AlreadyOpenReaderRejectsFixedPrivilegeDriftWithoutRepair(string drift)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var context = Open(PrimaryDatabaseRole.Global);
        await context.Database.OpenConnectionAsync();
        var reader = new AtprotoFederationSettingRepository(context, fixture.NativeQueueEnrollment);
        await RequireSuccessfulInstanceRead(reader);

        var names = new HumanPrincipalSourceSql(writer);
        string table = names.Table(typeof(AtprotoFederationInstanceSetting));
        string value = names.Column(typeof(AtprotoFederationInstanceSetting), nameof(AtprotoFederationInstanceSetting.Value));
        string createdAt = names.Column(typeof(AtprotoFederationInstanceSetting), nameof(AtprotoFederationInstanceSetting.CreatedAt));
        string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string roleName = $"federation_read_drift_{Guid.CreateVersion7():N}";
        string intermediateName = $"federation_read_hop_{Guid.CreateVersion7():N}";
        string role = names.Quote(roleName);
        string intermediate = names.Quote(intermediateName);
        string instanceColumns = string.Join(",", new[]
        {
            nameof(AtprotoFederationInstanceSetting.Id), nameof(AtprotoFederationInstanceSetting.SettingKey),
            nameof(AtprotoFederationInstanceSetting.Value), nameof(AtprotoFederationInstanceSetting.IsLocked),
            nameof(AtprotoFederationInstanceSetting.CreatedAt), nameof(AtprotoFederationInstanceSetting.CreatedBy),
            nameof(AtprotoFederationInstanceSetting.UpdatedAt), nameof(AtprotoFederationInstanceSetting.UpdatedBy)
        }.Select(property => names.Column(typeof(AtprotoFederationInstanceSetting), property)));
        string createRole = $"CREATE ROLE {role} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; "
            + $"GRANT SELECT({value}) ON {table} TO {role}; ";
        string removeRole = $"REVOKE SELECT({value}) ON {table} FROM {role}; DROP ROLE {role};";
        (string attack, string restore) = drift switch
        {
            "table-read" => ($"GRANT SELECT ON {table} TO {global}",
                $"REVOKE SELECT ON {table} FROM {global}; GRANT SELECT({instanceColumns}) ON {table} TO {global}"),
            "grant-option" => ($"GRANT SELECT({value}) ON {table} TO {global} WITH GRANT OPTION",
                $"REVOKE GRANT OPTION FOR SELECT({value}) ON {table} FROM {global}"),
            "system-column" => ($"GRANT SELECT(xmin) ON {table} TO {global}",
                $"REVOKE SELECT(xmin) ON {table} FROM {global}"),
            "required-column-missing" => ($"REVOKE SELECT({value}) ON {table} FROM {global}",
                $"GRANT SELECT({value}) ON {table} TO {global}"),
            "public-read" => ($"GRANT SELECT({value}) ON {table} TO PUBLIC",
                $"REVOKE SELECT({value}) ON {table} FROM PUBLIC"),
            "foreign-role-read" => (createRole, removeRole),
            "set-only-read" => (createRole + $"GRANT {role} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                $"REVOKE {role} FROM {global}; " + removeRole),
            "admin-only-read" => (createRole + $"GRANT {role} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                $"REVOKE {role} FROM {global}; " + removeRole),
            "nested-admin-read" => (createRole
                + $"CREATE ROLE {intermediate} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; "
                + $"GRANT {role} TO {intermediate} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE; "
                + $"GRANT {intermediate} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                $"REVOKE {intermediate} FROM {global}; REVOKE {role} FROM {intermediate}; DROP ROLE {intermediate}; " + removeRole),
            "schema-create" => ($"GRANT CREATE ON SCHEMA {names.Quote(names.Schema)} TO {global}",
                $"REVOKE CREATE ON SCHEMA {names.Quote(names.Schema)} FROM {global}"),
            "undeclared-update-column" => ($"GRANT UPDATE({createdAt}) ON {table} TO {global}",
                $"REVOKE UPDATE({createdAt}) ON {table} FROM {global}"),
            "runtime-instance-write" => ($"GRANT UPDATE({value}) ON {table} TO {runtime}",
                $"REVOKE UPDATE({value}) ON {table} FROM {runtime}"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };

        await VerifyCommittedDriftIsRejectedWithoutRepair(writer, context, reader, attack, restore,
            [roleName, intermediateName]);
    }

    [Test]
    [Arguments("public-policy-role")]
    [Arguments("wrong-policy-expression")]
    [Arguments("missing-policy-fingerprint")]
    [Arguments("missing-global-policy")]
    [Arguments("global-policy-all-commands")]
    [Arguments("extra-permissive-policy")]
    [Arguments("tenant-write-check-bypass")]
    public async Task AlreadyOpenReaderRejectsGlobalReadPolicyAndTenantPolicyDriftWithoutRepair(string drift)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var context = Open(PrimaryDatabaseRole.Global);
        await context.Database.OpenConnectionAsync();
        var reader = new AtprotoFederationSettingRepository(context, fixture.NativeQueueEnrollment);
        await RequireSuccessfulInstanceRead(reader);

        var names = new HumanPrincipalSourceSql(writer);
        string table = names.Table(typeof(AtprotoFederationTenantSetting));
        string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        const string policyName = "atproto_federation_global_read";
        string policy = names.Quote(policyName);
        string extra = names.Quote($"federation_read_drift_{Guid.CreateVersion7():N}");
        string fingerprint = await writer.Database.SqlQueryRaw<string>(
            "SELECT COALESCE(pg_catalog.obj_description(p.oid,'pg_policy'),'') AS \"Value\" "
                + "FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({0}) AND p.polname={1}",
            table, policyName).SingleAsync();
        await Assert.That(fingerprint.Length).IsGreaterThan(0);
        string stamp = $"COMMENT ON POLICY {policy} ON {table} IS {Literal(fingerprint)}";
        string recreate = $"CREATE POLICY {policy} ON {table} FOR SELECT TO {global} USING (true); {stamp}";
        string tenantTableName = writer.Model.FindEntityType(typeof(AtprotoFederationTenantSetting))!.GetTableName()!;
        var tenantPolicy = PostgresTenantRowLevelSecurityModel.GetTenantTables(writer.Model)
            .Single(metadata => metadata.TableName == tenantTableName);
        (string attack, string restore) = drift switch
        {
            "public-policy-role" => ($"ALTER POLICY {policy} ON {table} TO PUBLIC",
                $"ALTER POLICY {policy} ON {table} TO {global}"),
            "wrong-policy-expression" => ($"ALTER POLICY {policy} ON {table} USING (false)",
                $"ALTER POLICY {policy} ON {table} USING (true)"),
            "missing-policy-fingerprint" => ($"COMMENT ON POLICY {policy} ON {table} IS NULL", stamp),
            "missing-global-policy" => ($"DROP POLICY {policy} ON {table}", recreate),
            "global-policy-all-commands" => ($"DROP POLICY {policy} ON {table}; "
                + $"CREATE POLICY {policy} ON {table} FOR ALL TO {global} USING (true) WITH CHECK (true); {stamp}",
                $"DROP POLICY {policy} ON {table}; {recreate}"),
            "extra-permissive-policy" => ($"CREATE POLICY {extra} ON {table} FOR SELECT TO {global} USING (true)",
                $"DROP POLICY {extra} ON {table}"),
            "tenant-write-check-bypass" => ($"ALTER POLICY {names.Quote(PostgresTenantRowLevelSecurityModel.PolicyName)} ON {table} WITH CHECK (true)",
                PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(tenantPolicy, names.Schema)),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };

        await VerifyCommittedDriftIsRejectedWithoutRepair(writer, context, reader, attack, restore, []);
    }

    [Test]
    public async Task InstanceRlsCannotSilentlyHideALockedFederationCeiling()
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var context = Open(PrimaryDatabaseRole.Global);
        await context.Database.OpenConnectionAsync();
        var reader = new AtprotoFederationSettingRepository(context, fixture.NativeQueueEnrollment);
        await RequireSuccessfulInstanceRead(reader);
        var names = new HumanPrincipalSourceSql(writer);
        string table = names.Table(typeof(AtprotoFederationInstanceSetting));
        bool originalLock = await writer.Set<AtprotoFederationInstanceSetting>()
            .Where(row => row.SettingKey == EventsEnabled).Select(row => row.IsLocked).SingleAsync();
        await writer.Set<AtprotoFederationInstanceSetting>().Where(row => row.SettingKey == EventsEnabled)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsLocked, true));
        try
        {
            var positive = await reader.GetInstanceAsync([EventsEnabled]);
            await Assert.That(positive.Single().IsLocked).IsTrue();
            await Assert.That(positive.Single().Value).IsEqualTo("false");
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            int backend = connection.ProcessID;
            await ExecuteCatalogMutation(writer, $"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY");
            try
            {
                // Default-deny RLS really hides the locked row; an empty result is not safe policy absence.
                var hidden = await context.Set<AtprotoFederationInstanceSetting>().AsNoTracking()
                    .Where(row => row.SettingKey == EventsEnabled).ToArrayAsync();
                await Assert.That(hidden.Length).IsEqualTo(0);
                string driftedState = await CatalogFingerprint(writer, []);
                var rejection = await Assert.ThrowsAsync<Exception>(() => reader.GetInstanceAsync([EventsEnabled]));
                await Assert.That(rejection is UnauthorizedAccessException or InvalidOperationException).IsTrue();
                await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
                await Assert.That(connection.ProcessID).IsEqualTo(backend);
                await Assert.That(await CatalogFingerprint(writer, [])).IsEqualTo(driftedState);
            }
            finally
            {
                await ExecuteCatalogMutation(writer,
                    $"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY; ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY");
            }
            var restored = await reader.GetInstanceAsync([EventsEnabled]);
            await Assert.That(restored.Single().IsLocked).IsTrue();
            await Assert.That(restored.Single().Value).IsEqualTo("false");
        }
        finally
        {
            await writer.Set<AtprotoFederationInstanceSetting>().Where(row => row.SettingKey == EventsEnabled)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsLocked, originalLock));
        }
    }

    [Test]
    public async Task DormantAdminMembershipCannotExposeAnOtherwiseRestrictedFederationTableOwner()
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        var names = new HumanPrincipalSourceSql(writer);
        string ownerName = $"federation_owner_{Guid.CreateVersion7():N}";
        string owner = names.Quote(ownerName);
        string originalOwner = names.Quote(fixture.NativeQueueEnrollment.MigratorLogin);
        string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string schema = names.Quote(names.Schema);
        string[] tables = [names.Table(typeof(AtprotoFederationInstanceSetting)), names.Table(typeof(AtprotoFederationTenantSetting))];
        string credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string setup = $"CREATE ROLE {owner} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD {Literal(credential)}; "
            + $"GRANT USAGE ON SCHEMA {schema} TO {owner}; REVOKE CREATE ON SCHEMA {schema} FROM {owner}; "
            + string.Join(' ', tables.Select(table => $"ALTER TABLE {table} OWNER TO {owner};"));
        await ExecuteCatalogMutation(writer, setup);
        try
        {
            var ownerOptions = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(ownerOptions,
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator) with { Username = ownerName, Password = credential });
            await using var enrolledOwner = new ExploreDbContext(ownerOptions.Options);
            await Assert.That(await enrolledOwner.Database.SqlQueryRaw<bool>("""
                SELECT session_user={0} AND current_user=session_user
                    AND NOT pg_catalog.has_schema_privilege(current_user,{1},'CREATE')
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname=current_user
                        AND r.rolcanlogin AND NOT (r.rolsuper OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication OR r.rolbypassrls)) AS "Value"
                """, ownerName, names.Schema).SingleAsync()).IsTrue();

            await using var context = Open(PrimaryDatabaseRole.Global);
            await context.Database.OpenConnectionAsync();
            var enrollment = fixture.NativeQueueEnrollment with { MigratorLogin = ownerName };
            var reader = new AtprotoFederationSettingRepository(context, enrollment);
            await RequireSuccessfulInstanceRead(reader);
            string sourceAcl = await SourceAclFingerprint(writer, tables);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            int backend = connection.ProcessID;
            await ExecuteCatalogMutation(writer, $"GRANT {owner} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE");
            try
            {
                await Assert.That(await writer.Database.SqlQueryRaw<bool>("""
                    SELECT pg_catalog.pg_has_role({0},{1},'MEMBER')
                        AND NOT pg_catalog.pg_has_role({0},{1},'SET')
                        AND NOT pg_catalog.pg_has_role({0},{1},'USAGE') AS "Value"
                    """, enrollment.GlobalLogin, ownerName).SingleAsync()).IsTrue();
                await Assert.That(await SourceAclFingerprint(writer, tables)).IsEqualTo(sourceAcl);
                string driftedState = await CatalogFingerprint(writer, [ownerName]);
                var rejection = await Assert.ThrowsAsync<Exception>(() => reader.GetInstanceAsync([EventsEnabled]));
                await Assert.That(rejection is UnauthorizedAccessException or InvalidOperationException).IsTrue();
                await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
                await Assert.That(connection.ProcessID).IsEqualTo(backend);
                await Assert.That(await CatalogFingerprint(writer, [ownerName])).IsEqualTo(driftedState);
            }
            finally
            {
                await ExecuteCatalogMutation(writer, $"REVOKE {owner} FROM {global}");
            }
            await RequireSuccessfulInstanceRead(reader);
        }
        finally
        {
            string restore = string.Join(' ', tables.Select(table => $"ALTER TABLE {table} OWNER TO {originalOwner};"))
                + $" REVOKE USAGE ON SCHEMA {schema} FROM {owner}; DROP ROLE {owner};";
            await ExecuteCatalogMutation(writer, restore);
        }
        await using var restoredContext = Open(PrimaryDatabaseRole.Global);
        await RequireSuccessfulInstanceRead(new AtprotoFederationSettingRepository(restoredContext, fixture.NativeQueueEnrollment));
    }

    [Test]
    public async Task InstallerReapplicationPreservesTheMigratorsOriginalTransactionAndRollback()
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        string originalCatalog = await CatalogFingerprint(writer, []);
        bool originalLock = await writer.Set<AtprotoFederationInstanceSetting>()
            .Where(row => row.SettingKey == EventsEnabled).Select(row => row.IsLocked).SingleAsync();

        await writer.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var connection = (NpgsqlConnection)writer.Database.GetDbConnection();
            int backend = connection.ProcessID;
            await Assert.That(await writer.Database.SqlQueryRaw<bool>(
                "SELECT session_user={0} AND current_user=session_user AS \"Value\"",
                fixture.NativeQueueEnrollment.MigratorLogin).SingleAsync()).IsTrue();
            await writer.Set<AtprotoFederationInstanceSetting>().Where(row => row.SettingKey == EventsEnabled)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsLocked, !originalLock));

            await PostgresAtprotoFederationSettingPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment);

            await Assert.That(ReferenceEquals(writer.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(ReferenceEquals(writer.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(ReferenceEquals(transaction.GetDbTransaction().Connection, connection)).IsTrue();
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await Assert.That(await CatalogFingerprint(writer, [])).IsEqualTo(originalCatalog);
            await Assert.That(await writer.Set<AtprotoFederationInstanceSetting>()
                .Where(row => row.SettingKey == EventsEnabled).Select(row => row.IsLocked).SingleAsync())
                .IsEqualTo(!originalLock);
            await transaction.RollbackAsync();
        });

        await Assert.That(await writer.Set<AtprotoFederationInstanceSetting>()
            .Where(row => row.SettingKey == EventsEnabled).Select(row => row.IsLocked).SingleAsync())
            .IsEqualTo(originalLock);
        await Assert.That(await CatalogFingerprint(writer, [])).IsEqualTo(originalCatalog);
        await using var context = Open(PrimaryDatabaseRole.Global);
        await RequireSuccessfulInstanceRead(new AtprotoFederationSettingRepository(context, fixture.NativeQueueEnrollment));
    }

    [Test]
    public Task InstallerRejectsAMissingFixedColumnGrantWithoutRepairingOrAbortingItsCaller() =>
        VerifyInstallerRejectsDriftWithoutRepair("required-column-missing");

    [Test]
    public Task InstallerRejectsAChangedGlobalPolicyBodyWithoutRepairingOrAbortingItsCaller() =>
        VerifyInstallerRejectsDriftWithoutRepair("changed-global-policy");

    [Test]
    public async Task RetiredGovernanceRolePreventsReadAndInstallationWithoutAutomaticCleanup()
    {
        const string obsoleteRole = "event_atproto_governance_runtime";
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var context = Open(PrimaryDatabaseRole.Global);
        await context.Database.OpenConnectionAsync();
        var reader = new AtprotoFederationSettingRepository(context, fixture.NativeQueueEnrollment);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        int backend = connection.ProcessID;
        await Assert.That(await writer.Database.SqlQueryRaw<bool>(
            "SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={0}) AS \"Value\"",
            obsoleteRole).SingleAsync()).IsTrue();
        await RequireSuccessfulInstanceRead(reader);
        await PostgresAtprotoFederationSettingPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment);
        string originalCatalog = await CatalogFingerprint(writer, [obsoleteRole]);
        string role = new HumanPrincipalSourceSql(writer).Quote(obsoleteRole);
        await ExecuteCatalogMutation(writer,
            $"CREATE ROLE {role} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS");
        string obsoleteCatalog = await CatalogFingerprint(writer, [obsoleteRole]);
        try
        {
            // A committed leftover role is visible to the existing reader connector, not only to Migrator.
            await Assert.That(await context.Database.SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={0}) AS \"Value\"",
                obsoleteRole).SingleAsync()).IsTrue();
            string? readRejection = null;
            try
            {
                await reader.GetInstanceAsync([EventsEnabled]);
            }
            catch (UnauthorizedAccessException exception)
            {
                readRejection = exception.Message;
            }
            string? installRejection = null;
            try
            {
                await PostgresAtprotoFederationSettingPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment);
            }
            catch (InvalidOperationException exception)
            {
                installRejection = exception.Message;
            }

            await Assert.That(await CatalogFingerprint(writer, [obsoleteRole])).IsEqualTo(obsoleteCatalog);
            await Assert.That(ReferenceEquals(context.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await Assert.That((readRejection, installRejection)).IsEqualTo(
                ("federation_settings_read_not_ready", "federation_settings_permissions_drift"));
        }
        finally
        {
            // Never remove an existing or changed operator object: only this test's unchanged creation is owned.
            if (await CatalogFingerprint(writer, [obsoleteRole]) != obsoleteCatalog)
                throw new InvalidOperationException("test_owned_obsolete_governance_role_changed_not_removed");
            await ExecuteCatalogMutation(writer, $"DROP ROLE {role}");
            await Assert.That(await CatalogFingerprint(writer, [obsoleteRole])).IsEqualTo(originalCatalog);
            await PostgresAtprotoFederationSettingPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment);
            await RequireSuccessfulInstanceRead(reader);
        }
    }

    private async Task VerifyInstallerRejectsDriftWithoutRepair(string drift)
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        await using var context = Open(PrimaryDatabaseRole.Global);
        var reader = new AtprotoFederationSettingRepository(context, fixture.NativeQueueEnrollment);
        await RequireSuccessfulInstanceRead(reader);
        string originalCatalog = await CatalogFingerprint(writer, []);
        bool originalLock = await writer.Set<AtprotoFederationInstanceSetting>()
            .Where(row => row.SettingKey == EventsEnabled).Select(row => row.IsLocked).SingleAsync();
        var names = new HumanPrincipalSourceSql(writer);
        string instanceTable = names.Table(typeof(AtprotoFederationInstanceSetting));
        string tenantTable = names.Table(typeof(AtprotoFederationTenantSetting));
        string value = names.Column(typeof(AtprotoFederationInstanceSetting), nameof(AtprotoFederationInstanceSetting.Value));
        string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string policy = names.Quote("atproto_federation_global_read");
        string attack = drift switch
        {
            "required-column-missing" => $"REVOKE SELECT({value}) ON {instanceTable} FROM {global}",
            "changed-global-policy" => $"ALTER POLICY {policy} ON {tenantTable} USING (false)",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };

        await writer.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var connection = (NpgsqlConnection)writer.Database.GetDbConnection();
            int backend = connection.ProcessID;
            await Assert.That(await writer.Database.SqlQueryRaw<bool>(
                "SELECT session_user={0} AND current_user=session_user AS \"Value\"",
                fixture.NativeQueueEnrollment.MigratorLogin).SingleAsync()).IsTrue();
            // The same physical Migrator and caller transaction accept the complete fixed enrollment first.
            await PostgresAtprotoFederationSettingPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment);
            await writer.Database.ExecuteSqlRawAsync(attack);
            string driftedCatalog = await CatalogFingerprint(writer, []);
            await Assert.That(driftedCatalog).IsNotEqualTo(originalCatalog);

            var rejection = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresAtprotoFederationSettingPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment));

            await Assert.That(rejection.Message).IsEqualTo("federation_settings_permissions_drift");
            await Assert.That(await CatalogFingerprint(writer, [])).IsEqualTo(driftedCatalog);
            await Assert.That(ReferenceEquals(writer.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(ReferenceEquals(writer.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(ReferenceEquals(transaction.GetDbTransaction().Connection, connection)).IsTrue();
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            // Real policy data can still be changed by the owner, then rolled back with the catalog attack.
            int changed = await writer.Set<AtprotoFederationInstanceSetting>()
                .Where(row => row.SettingKey == EventsEnabled)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsLocked, !originalLock));
            await Assert.That(changed).IsEqualTo(1);
            await Assert.That(await writer.Set<AtprotoFederationInstanceSetting>()
                .Where(row => row.SettingKey == EventsEnabled).Select(row => row.IsLocked).SingleAsync())
                .IsEqualTo(!originalLock);
            await transaction.RollbackAsync();
        });

        await Assert.That(await CatalogFingerprint(writer, [])).IsEqualTo(originalCatalog);
        await Assert.That(await writer.Set<AtprotoFederationInstanceSetting>()
            .Where(row => row.SettingKey == EventsEnabled).Select(row => row.IsLocked).SingleAsync())
            .IsEqualTo(originalLock);
        await PostgresAtprotoFederationSettingPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment);
        await RequireSuccessfulInstanceRead(reader);
    }

    private static async Task<string> SourceAclFingerprint(ExploreDbContext context, string[] tables) =>
        await context.Database.SqlQueryRaw<string>("""
            SELECT md5(concat_ws('|',
                (SELECT string_agg(concat_ws(':', c.oid, c.relacl), '|' ORDER BY c.oid)
                    FROM pg_catalog.pg_class c WHERE c.oid=ANY(SELECT pg_catalog.to_regclass(t) FROM unnest(@tables) t)),
                (SELECT string_agg(concat_ws(':', a.attrelid, a.attnum, a.attacl), '|' ORDER BY a.attrelid,a.attnum)
                    FROM pg_catalog.pg_attribute a WHERE a.attrelid=ANY(SELECT pg_catalog.to_regclass(t) FROM unnest(@tables) t)))) AS "Value"
            """, new NpgsqlParameter("tables", tables)).SingleAsync();

    private static async Task RequireSuccessfulInstanceRead(AtprotoFederationSettingRepository reader)
    {
        var rows = await reader.GetInstanceAsync([EventsEnabled]);
        await Assert.That(rows.Single().Value).IsEqualTo("false");
    }

    private async Task VerifyCommittedDriftIsRejectedWithoutRepair(
        ExploreDbContext writer, ExploreDbContext context, AtprotoFederationSettingRepository reader,
        string attack, string restore, IReadOnlyCollection<string> extraRoles)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        int backend = connection.ProcessID;
        await ExecuteCatalogMutation(writer, attack);
        try
        {
            string driftedState = await CatalogFingerprint(writer, extraRoles);
            var rejection = await Assert.ThrowsAsync<Exception>(() => reader.GetInstanceAsync([EventsEnabled]));
            await Assert.That(rejection is UnauthorizedAccessException or InvalidOperationException).IsTrue();
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await Assert.That(ReferenceEquals(context.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(await CatalogFingerprint(writer, extraRoles)).IsEqualTo(driftedState);
        }
        finally
        {
            await ExecuteCatalogMutation(writer, restore);
        }
        await RequireSuccessfulInstanceRead(reader);
    }

    private static async Task ExecuteCatalogMutation(ExploreDbContext writer, string sql)
    {
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync(sql);
        await transaction.CommitAsync();
    }

    private async Task<string> CatalogFingerprint(ExploreDbContext context, IReadOnlyCollection<string> extraRoles)
    {
        var names = new HumanPrincipalSourceSql(context);
        string[] roles = [fixture.NativeQueueEnrollment.GlobalLogin, fixture.NativeQueueEnrollment.RuntimeLogin, .. extraRoles];
        string[] tables = [names.Table(typeof(AtprotoFederationInstanceSetting)), names.Table(typeof(AtprotoFederationTenantSetting))];
        return await context.Database.SqlQueryRaw<string>("""
            SELECT md5(concat_ws('|',
                (SELECT string_agg(concat_ws(':', c.oid, c.relowner, c.relacl, c.relrowsecurity, c.relforcerowsecurity), '|' ORDER BY c.oid)
                    FROM pg_catalog.pg_class c WHERE c.oid = ANY(SELECT pg_catalog.to_regclass(t) FROM unnest(@tables) t)),
                (SELECT string_agg(concat_ws(':', a.attrelid, a.attnum, a.attname, a.attacl), '|' ORDER BY a.attrelid,a.attnum)
                    FROM pg_catalog.pg_attribute a WHERE a.attrelid = ANY(SELECT pg_catalog.to_regclass(t) FROM unnest(@tables) t)),
                (SELECT string_agg(concat_ws(':', p.polrelid, p.polname, p.polcmd, p.polpermissive, p.polroles,
                        p.polqual::text, p.polwithcheck::text, pg_catalog.obj_description(p.oid,'pg_policy')), '|' ORDER BY p.polrelid,p.polname)
                    FROM pg_catalog.pg_policy p WHERE p.polrelid = ANY(SELECT pg_catalog.to_regclass(t) FROM unnest(@tables) t)),
                (SELECT string_agg(concat_ws(':', r.oid,r.rolname,r.rolsuper,r.rolinherit,r.rolcreaterole,r.rolcreatedb,r.rolcanlogin,r.rolreplication,r.rolbypassrls), '|' ORDER BY r.oid)
                    FROM pg_catalog.pg_roles r WHERE r.rolname=ANY(@roles)),
                (SELECT string_agg(concat_ws(':', m.roleid,m.member,m.grantor,m.admin_option,m.inherit_option,m.set_option), '|' ORDER BY m.roleid,m.member,m.grantor)
                    FROM pg_catalog.pg_auth_members m WHERE m.roleid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname=ANY(@roles))
                        OR m.member IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname=ANY(@roles))),
                (SELECT n.nspacl::text FROM pg_catalog.pg_namespace n WHERE n.nspname=@schema))) AS "Value"
            """,
            new NpgsqlParameter("tables", tables), new NpgsqlParameter("roles", roles),
            new NpgsqlParameter("schema", names.Schema)).SingleAsync();
    }

    private static string Literal(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid? tenantId = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options)
        {
            TenantContext = tenantId is { } id ? new FederationReadTenant(id) : null
        };
        context.BindTenantLease();
        return context;
    }

    private sealed class NativeReadFactory(Func<ExploreDbContext> create) : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(create());
        }
    }
}

internal sealed class FederationReadDatabase(SqliteConnection keeper) : IAsyncDisposable
{
    internal static readonly DateTime ObservedAt = new(2026, 10, 18, 16, 0, 0, DateTimeKind.Utc);
    internal Guid ActiveTenantId { get; } = Guid.CreateVersion7();
    internal Guid InactiveTenantId { get; } = Guid.CreateVersion7();
    internal FederationReadCommands Commands { get; } = new();

    internal ExploreDbContext CreateContext(Guid? tenantId, bool bind = true)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(keeper.ConnectionString)
            .UseSnakeCaseNamingConvention().AddInterceptors(Commands).Options;
        var context = new ExploreDbContext(options)
        {
            TenantContext = tenantId is { } id ? new FederationReadTenant(id) : null
        };
        if (bind) context.BindTenantLease();
        return context;
    }

    internal static async Task<FederationReadDatabase> CreateAsync()
    {
        var fixture = new FederationReadDatabase(await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync());
        try
        {
            await using (var seed = fixture.CreateContext(null))
            {
                await seed.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(seed, default);
                foreach (var (id, active) in new[] { (fixture.ActiveTenantId, true), (fixture.InactiveTenantId, false) })
                {
                    int statusId = (int)(active ? TenantStatusEnum.Active : TenantStatusEnum.Suspended);
                    var status = new TenantStatus
                    {
                        Id = statusId,
                        MasterCode = active ? "ACTIVE" : "SUSPENDED",
                        FullName = active ? "Active" : "Suspended",
                        IsActiveState = active
                    };
                    seed.Add(status);
                    seed.Add(new Tenant
                    {
                        Id = id,
                        FullName = "Federation read tenant",
                        Slug = $"federation-{id:N}",
                        TenantStatusId = statusId,
                        TenantStatus = status,
                        CreatedAt = ObservedAt
                    });
                }
                foreach (var (key, value) in Values(enabled: false))
                    seed.Add(AtprotoFederationInstanceSetting.Create(key, value, false, ObservedAt, null));
                await seed.SaveChangesAsync();
            }
            foreach (Guid tenantId in new[] { fixture.ActiveTenantId, fixture.InactiveTenantId })
            {
                await using var seed = fixture.CreateContext(tenantId);
                foreach (var (key, value) in Values(tenantId == fixture.ActiveTenantId))
                    seed.Add(AtprotoFederationTenantSetting.Create(tenantId, key, value, false, ObservedAt, null));
                await seed.SaveChangesAsync();
            }
            fixture.Commands.Reset();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static (string Key, string Value)[] Values(bool enabled) =>
    [
        (GovernanceSettingKeys.Federation.AtprotoEventsEnabled, enabled ? "true" : "false"),
        (GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled, enabled ? "true" : "false"),
        (GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode, enabled ? "\"FuLl\"" : "\"downtime_only\"")
    ];

    public ValueTask DisposeAsync() => keeper.DisposeAsync();
}

internal sealed class FederationReadFactory(
    FederationReadDatabase fixture, Guid? selectedTenant = null, bool bypass = false, Action? onCreate = null)
    : IGlobalExploreDbContextFactory
{
    internal ExploreDbContext? LastContext { get; private set; }

    public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        onCreate?.Invoke();
        var context = fixture.CreateContext(selectedTenant);
        if (bypass) context.EnableTenantFilterBypass("Federation read adversarial factory");
        LastContext = context;
        return Task.FromResult(context);
    }
}

internal sealed class FederationReadCommands : DbCommandInterceptor
{
    internal int ReaderCount { get; private set; }
    internal void Reset() => ReaderCount = 0;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ReaderCount++;
        return ValueTask.FromResult(result);
    }
}

internal sealed record FederationReadTenant(Guid TenantId) : ITenantContext;
