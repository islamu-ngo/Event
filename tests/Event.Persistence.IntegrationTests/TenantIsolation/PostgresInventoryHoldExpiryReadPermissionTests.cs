using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresInventoryHoldExpiryReadPermissionTests(PostgreSqlContainerFixture fixture)
{
    private static readonly (Type Type, string[] Properties)[] Inventory =
    [
        (typeof(RegistrationInventoryHold), [nameof(RegistrationInventoryHold.Id), nameof(RegistrationInventoryHold.TenantId),
            nameof(RegistrationInventoryHold.RegistrationOrderId), nameof(RegistrationInventoryHold.RegistrationInventoryHoldStatusId),
            nameof(RegistrationInventoryHold.ExpiresAt), nameof(RegistrationInventoryHold.IsDeleted)]),
        (typeof(RegistrationOrder), [nameof(RegistrationOrder.Id), nameof(RegistrationOrder.TenantId),
            nameof(RegistrationOrder.RegistrationOrderStatusId), nameof(RegistrationOrder.UpdatedAt), nameof(RegistrationOrder.IsDeleted)])
    ];

    [Test]
    public async Task InstallerEnablesOnlyFixedExpiryMetadataWithoutRetiredRoutine()
    {
        await using var migration = OpenMigrator();
        await PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        foreach (var source in Inventory)
        {
            await using var command = migration.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COALESCE(string_agg(a.attname, '|' ORDER BY a.attname),'')
                FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass(@table)
                    AND a.attnum<>0 AND NOT a.attisdropped
                    AND pg_catalog.has_column_privilege(@login,a.attrelid,a.attnum,'SELECT')
                """;
            command.Parameters.Add(new NpgsqlParameter("table", names.Table(source.Type)));
            command.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
            await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(string.Join('|', source.Properties
                .Select(property => names.ColumnName(source.Type, property)).Order(StringComparer.Ordinal)));
            command.CommandText = """
                SELECT NOT pg_catalog.has_table_privilege(@login,pg_catalog.to_regclass(@table),
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        CROSS JOIN (VALUES ('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                        WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                            AND pg_catalog.has_column_privilege(@login,a.attrelid,a.attnum,permission.privilege))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class t ON t.oid=a.attrelid
                        WHERE t.oid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                            AND pg_catalog.has_column_privilege(@login,t.oid,a.attnum,'SELECT')
                            AND (a.attnum<0 OR pg_catalog.has_column_privilege(@login,t.oid,a.attnum,'SELECT WITH GRANT OPTION')
                                OR NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                    WHERE acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@login)
                                        AND acl.grantor=t.relowner AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                                OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                    WHERE acl.grantee=0 AND acl.privilege_type='SELECT')))
                """;
            await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
        }
        await using var retired = migration.Database.GetDbConnection().CreateCommand();
        retired.CommandText = """
            SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname=@schema AND p.proname='inventory_hold_expiry_targets')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                    ('hold_expiry_discovery_owner','hold_expiry_discovery_migrator','hold_expiry_discovery_executor'))
            """;
        retired.Parameters.Add(new NpgsqlParameter("schema", names.Schema));
        await Assert.That(await retired.ExecuteScalarAsync()).IsEqualTo(true);

        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        DateTime now = new(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var holds = await global.RegistrationInventoryHolds.IgnoreQueryFilters([QueryFilterNames.Tenant])
            .IgnoreAutoIncludes().AsNoTracking().Where(row => !row.IsDeleted
                && row.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active && row.ExpiresAt <= now)
            .OrderBy(row => row.ExpiresAt).ThenBy(row => row.Id).Take(1)
            .Select(row => new { row.TenantId, row.RegistrationOrderId, row.Id }).ToArrayAsync();
        var orders = await global.RegistrationOrders.IgnoreQueryFilters([QueryFilterNames.Tenant])
            .IgnoreAutoIncludes().AsNoTracking().Where(row => !row.IsDeleted
                && row.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.NeedsReconciliation)
            .OrderBy(row => row.UpdatedAt).ThenBy(row => row.Id).Take(1)
            .Select(row => new { row.TenantId, row.Id }).ToArrayAsync();
        await Assert.That(holds.Length).IsLessThanOrEqualTo(1);
        await Assert.That(orders.Length).IsLessThanOrEqualTo(1);
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(global.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("quantity")]
    [Arguments("account")]
    [Arguments("amount")]
    [Arguments("order-pii")]
    [Arguments("participant-pii")]
    public async Task MetadataRightsDoNotExposeInventoryBusinessValuesOrOrderPrivateData(string source)
    {
        await using var migration = OpenMigrator();
        await PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(global);
        var (type, property) = source switch
        {
            "quantity" => (typeof(RegistrationInventoryHold), nameof(RegistrationInventoryHold.Quantity)),
            "account" => (typeof(RegistrationOrder), nameof(RegistrationOrder.AccountUserId)),
            "amount" => (typeof(RegistrationOrder), nameof(RegistrationOrder.TotalDueMinorSnapshot)),
            "order-pii" => (typeof(RegistrationOrderPii), nameof(RegistrationOrderPii.Email)),
            "participant-pii" => (typeof(RegistrationParticipantPii), nameof(RegistrationParticipantPii.Email)),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        var denied = await Assert.ThrowsAsync<PostgresException>(() => global.Database.SqlQueryRaw<string>(
            $"SELECT {names.Column(type, property)}::text AS \"Value\" FROM {names.Table(type)} LIMIT 1").ToArrayAsync());
        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    [Arguments("hold-table")]
    [Arguments("order-table")]
    [Arguments("quantity")]
    [Arguments("account")]
    [Arguments("public")]
    [Arguments("inherited")]
    [Arguments("set-only")]
    [Arguments("admin-only")]
    [Arguments("system-column")]
    [Arguments("grant-option")]
    [Arguments("write")]
    public async Task FixedMetadataAuthorityRejectsPermissionDriftWithoutRepair(string change)
    {
        await using var strategy = OpenMigrator();
        await PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(strategy, fixture.NativeQueueEnrollment);
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var names = new HumanPrincipalSourceSql(context);
            string holds = names.Table(typeof(RegistrationInventoryHold)), orders = names.Table(typeof(RegistrationOrder));
            string id = names.Column(typeof(RegistrationInventoryHold), nameof(RegistrationInventoryHold.Id));
            string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
            string other = names.Quote("hold_expiry_drift_" + Guid.CreateVersion7().ToString("N"));
            string sql = change switch
            {
                "hold-table" => $"GRANT SELECT ON {holds} TO {global}",
                "order-table" => $"GRANT SELECT ON {orders} TO {global}",
                "quantity" => $"GRANT SELECT ({names.Column(typeof(RegistrationInventoryHold), nameof(RegistrationInventoryHold.Quantity))}) ON {holds} TO {global}",
                "account" => $"GRANT SELECT ({names.Column(typeof(RegistrationOrder), nameof(RegistrationOrder.AccountUserId))}) ON {orders} TO {global}",
                "public" => $"GRANT SELECT ({id}) ON {holds} TO PUBLIC",
                "system-column" => $"GRANT SELECT (xmin) ON {holds} TO {global}",
                "grant-option" => $"GRANT SELECT ({id}) ON {holds} TO {global} WITH GRANT OPTION",
                "write" => $"GRANT UPDATE ({id}) ON {holds} TO {global}",
                "inherited" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({id}) ON {holds} TO {other}; GRANT {other} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                "set-only" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({id}) ON {holds} TO {other}; GRANT {other} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "admin-only" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({id}) ON {holds} TO {other}; GRANT {other} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            await context.Database.ExecuteSqlRawAsync(sql);
            string before = await CatalogueImageAsync(context);
            await transaction.CreateSavepointAsync("before_global_read");
            await context.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {global}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
            await transaction.RollbackToSavepointAsync("before_global_read");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(await CatalogueImageAsync(context)).IsEqualTo(before);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(await CatalogueImageAsync(context)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task ReadinessRejectsRuntimeSelectedTenantAndMissingEnrollment()
    {
        await using var migration = OpenMigrator();
        await PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(global, null));
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment));
        await using var selected = await ((IMixedSettingsDbContextFactory)GlobalFactory())
            .CreateMixedSettingsAsync(Guid.CreateVersion7(), default);
        await selected.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(selected, fixture.NativeQueueEnrollment));
    }

    [Test]
    public async Task ReapplicationPreservesCallerTransactionAndExactCatalogue()
    {
        await using var strategy = OpenMigrator();
        await PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(strategy, fixture.NativeQueueEnrollment);
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var connector = RelationalConnectionIdentity.Capture(context);
            string before = await CatalogueImageAsync(context);
            await PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(connector.Matches(context)).IsTrue();
            await Assert.That(connector.Owns(transaction.GetDbTransaction())).IsTrue();
            await Assert.That(await CatalogueImageAsync(context)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    private static async Task<string> CatalogueImageAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT jsonb_build_object(
                'relations',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t.oid) FROM pg_catalog.pg_class t
                    WHERE t.oid=ANY(ARRAY(SELECT pg_catalog.to_regclass(value) FROM unnest(@tables::text[]) value))),
                'columns',(SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attrelid,a.attnum) FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid=ANY(ARRAY(SELECT pg_catalog.to_regclass(value) FROM unnest(@tables::text[]) value))),
                'policies',(SELECT jsonb_agg(to_jsonb(p) ORDER BY p.oid) FROM pg_catalog.pg_policy p
                    WHERE p.polrelid=ANY(ARRAY(SELECT pg_catalog.to_regclass(value) FROM unnest(@tables::text[]) value))),
                'memberships',(SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member,m.grantor) FROM pg_catalog.pg_auth_members m))::text
            """;
        command.Parameters.Add(new NpgsqlParameter("tables", Inventory.Select(source => names.Table(source.Type)).ToArray()));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private GlobalExploreDbContextFactory GlobalFactory() => new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
}
