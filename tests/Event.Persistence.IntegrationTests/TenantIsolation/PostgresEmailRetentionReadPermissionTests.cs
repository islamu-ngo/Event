using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

/// <summary>Real configured-login metadata authority, independent of retention business execution.</summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed partial class PostgresEmailRetentionReadPermissionTests(PostgreSqlContainerFixture fixture)
{
    private static readonly string[] ReadProperties =
    [nameof(EmailDispatchOutbox.TenantId), nameof(EmailDispatchOutbox.CreatedAt), nameof(EmailDispatchOutbox.Status),
        nameof(EmailDispatchOutbox.SentAt), nameof(EmailDispatchOutbox.LastFailureAt),
        nameof(EmailDispatchOutbox.ContentRedactedAt), nameof(EmailDispatchOutbox.IsDeleted)];

    [Test]
    public async Task GenericGlobalConnectionRejectsEmailReadPolicyWithoutUsingExpression()
    {
        await using var migration = OpenMigrator();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(EmailDispatchOutbox));
        const string policyName = "email_retention_metadata_global_read";
        string policy = names.Quote(policyName);
        string globalLogin = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        await using var observation = migration.Database.GetDbConnection().CreateCommand();
        observation.CommandText = """
            SELECT pg_catalog.pg_get_expr(p.polqual,p.polrelid) FROM pg_catalog.pg_policy p
            WHERE p.polrelid=pg_catalog.to_regclass(@table) AND p.polname=@policy
                AND p.polcmd='r' AND p.polpermissive AND p.polwithcheck IS NULL
                AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@login)]
            """;
        observation.Parameters.Add(new NpgsqlParameter("table", table));
        observation.Parameters.Add(new NpgsqlParameter("policy", policyName));
        observation.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
        string expression = (string)(await observation.ExecuteScalarAsync())!;
        await Assert.That(expression).IsEqualTo("true");
        await using (var healthy = await GlobalFactory().CreateDbContextAsync())
            await healthy.Database.OpenConnectionAsync();

        async Task ReplacePolicyAsync(bool restoreHealthyExpression)
        {
            await migration.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await migration.Database.BeginTransactionAsync();
                await migration.Database.ExecuteSqlRawAsync($"DROP POLICY {policy} ON {table}");
                if (restoreHealthyExpression)
                    await migration.Database.ExecuteSqlRawAsync($"CREATE POLICY {policy} ON {table} FOR SELECT TO {globalLogin} USING (true)");
                else
                    await migration.Database.ExecuteSqlRawAsync($"CREATE POLICY {policy} ON {table} FOR SELECT TO {globalLogin}");
                await transaction.CommitAsync();
            });
        }

        try
        {
            await ReplacePolicyAsync(restoreHealthyExpression: false);
            await Assert.That(await observation.ExecuteScalarAsync()).IsEqualTo(DBNull.Value);
            await using var rejected = await GlobalFactory().CreateDbContextAsync();
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => rejected.Database.OpenConnectionAsync());
            await Assert.That(denied.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
        }
        finally { await ReplacePolicyAsync(restoreHealthyExpression: true); }

        await using var restored = await GlobalFactory().CreateDbContextAsync();
        await restored.Database.OpenConnectionAsync();
        await Assert.That(await observation.ExecuteScalarAsync()).IsEqualTo(expression);
    }

    [Test]
    public async Task RepositoryReadsEmptyMetadataPageWithoutRetiredBusinessRoutine()
    {
        await using var migration = OpenMigrator();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        var factory = GlobalFactory();
        DateTime cutoff = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var global = await factory.CreateDbContextAsync())
        {
            await global.Database.OpenConnectionAsync();
            await PostgresEmailRetentionReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
            await using var command = global.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_catalog.to_regprocedure(@routine) IS NULL";
            command.Parameters.Add(new NpgsqlParameter("routine",
                new HumanPrincipalSourceSql(global).Name("email_retention_tenant_ids") + "(timestamp with time zone,integer)"));
            await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
            var eligible = await global.EmailDispatchOutbox
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue).IgnoreAutoIncludes().AsNoTracking()
                .Where(row => !row.IsDeleted && row.ContentRedactedAt == null
                    && ((row.Status == EmailDispatchStatus.Sent && row.SentAt <= cutoff)
                        || (row.Status == EmailDispatchStatus.Skipped && row.LastFailureAt <= cutoff)))
                .Select(row => row.TenantId).Take(1).ToArrayAsync();
            await Assert.That(eligible).IsEmpty();
            await Assert.That(global.TenantFilterTenantId).IsNull();
        }

        var page = await new EmailRetentionTenantDiscoveryRepository(factory, fixture.NativeQueueEnrollment)
            .GetTenantIdsAsync(cutoff, 2, CancellationToken.None);
        await Assert.That(page).IsEmpty();
    }

    [Test]
    public async Task InstallerEstablishesExactlySevenDirectMetadataReadsWithoutBusinessRoutine()
    {
        await using var migration = OpenMigrator();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        await using var inventory = migration.Database.GetDbConnection().CreateCommand();
        inventory.CommandText = """
            SELECT COALESCE(string_agg(a.attname, '|' ORDER BY a.attname),'')
            FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class t ON t.oid=a.attrelid
            WHERE t.oid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                AND pg_catalog.has_column_privilege(@login,t.oid,a.attnum,'SELECT')
            """;
        inventory.Parameters.Add(new NpgsqlParameter("table", names.Table(typeof(EmailDispatchOutbox))));
        inventory.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
        string expected = string.Join('|', ReadProperties.Select(property =>
            names.ColumnName(typeof(EmailDispatchOutbox), property)).Order(StringComparer.Ordinal));
        await Assert.That(await inventory.ExecuteScalarAsync()).IsEqualTo(expected);

        await using var shape = migration.Database.GetDbConnection().CreateCommand();
        shape.CommandText = """
            SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                CROSS JOIN (VALUES ('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                    AND pg_catalog.has_column_privilege(@login,a.attrelid,a.attnum,permission.privilege))
                AND NOT pg_catalog.has_table_privilege(@login,pg_catalog.to_regclass(@table),
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                        AND (pg_catalog.has_column_privilege(@login,a.attrelid,a.attnum,'SELECT WITH GRANT OPTION')
                            OR a.attnum<0 AND pg_catalog.has_column_privilege(@login,a.attrelid,a.attnum,'SELECT')))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class t ON t.oid=a.attrelid
                    WHERE t.oid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                        AND pg_catalog.has_column_privilege(@login,t.oid,a.attnum,'SELECT')
                        AND (NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                            WHERE acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@login)
                                AND acl.grantor=t.relowner AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                            OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                WHERE acl.grantee=0 AND acl.privilege_type='SELECT')))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                    WHERE n.nspname=@schema AND p.proname='email_retention_tenant_ids')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                    ('email_retention_discovery_owner','email_retention_discovery_migrator','email_retention_discovery_executor'))
            """;
        shape.Parameters.Add(new NpgsqlParameter("table", names.Table(typeof(EmailDispatchOutbox))));
        shape.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
        shape.Parameters.Add(new NpgsqlParameter("schema", names.Schema));
        await Assert.That(await shape.ExecuteScalarAsync()).IsEqualTo(true);

        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        var connector = RelationalConnectionIdentity.Capture(global);
        await PostgresEmailRetentionReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        DateTime cutoff = new(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var page = await global.EmailDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue).IgnoreAutoIncludes().AsNoTracking()
            .Where(row => !row.IsDeleted && row.ContentRedactedAt == null
                && ((row.Status == EmailDispatchStatus.Sent && row.SentAt <= cutoff)
                    || (row.Status == EmailDispatchStatus.Skipped && row.LastFailureAt <= cutoff)))
            .GroupBy(row => row.TenantId)
            .Select(group => new { TenantId = group.Key, Oldest = group.Min(row => row.CreatedAt) })
            .OrderBy(row => row.Oldest).ThenBy(row => row.TenantId).Take(1).Select(row => row.TenantId).ToArrayAsync();
        await Assert.That(page.Length).IsLessThanOrEqualTo(1);
        await Assert.That(connector.Matches(global)).IsTrue();
        await Assert.That(global.Database.CurrentTransaction).IsNull();
        await Assert.That(global.TenantFilterTenantId).IsNull();
    }

    [Test]
    [Arguments("subject")]
    [Arguments("plain-body")]
    [Arguments("html-body")]
    [Arguments("recipient")]
    [Arguments("attempt")]
    [Arguments("receipt")]
    [Arguments("delivery")]
    public async Task MetadataReadDoesNotExposeContentRecipientsOrRelatedPrivateSources(string source)
    {
        await using var migration = OpenMigrator();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresEmailRetentionReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(global);
        var (type, property) = PrivateSource(source);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => global.Database.SqlQueryRaw<string>(
            $"SELECT {names.Column(type, property)}::text AS \"Value\" FROM {names.Table(type)} LIMIT 1").ToArrayAsync());
        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    [Arguments("table")]
    [Arguments("write")]
    [Arguments("grant-option")]
    [Arguments("public")]
    [Arguments("inherited")]
    [Arguments("set-only")]
    [Arguments("admin-only")]
    [Arguments("system-column")]
    [Arguments("plain-body")]
    [Arguments("recipient")]
    [Arguments("attempt")]
    [Arguments("receipt")]
    [Arguments("delivery")]
    public async Task PermissionDriftRejectsReadAndInstallWithoutRepair(string change)
    {
        await using var strategy = OpenMigrator();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(strategy, fixture.NativeQueueEnrollment);
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var names = new HumanPrincipalSourceSql(context);
            string table = names.Table(typeof(EmailDispatchOutbox));
            string column = names.Column(typeof(EmailDispatchOutbox), nameof(EmailDispatchOutbox.TenantId));
            string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
            string other = names.Quote("email_retention_drift_" + Guid.CreateVersion7().ToString("N"));
            string sql = change switch
            {
                "table" => $"GRANT SELECT ON {table} TO {global}",
                "write" => $"GRANT UPDATE ({column}) ON {table} TO {global}",
                "grant-option" => $"GRANT SELECT ({column}) ON {table} TO {global} WITH GRANT OPTION",
                "public" => $"GRANT SELECT ({column}) ON {table} TO PUBLIC",
                "system-column" => $"GRANT SELECT (xmin) ON {table} TO {global}",
                "inherited" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({column}) ON {table} TO {other}; GRANT {other} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                "set-only" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({column}) ON {table} TO {other}; GRANT {other} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "admin-only" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({column}) ON {table} TO {other}; GRANT {other} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                _ => PrivateGrant(names, change, global)
            };
            await context.Database.ExecuteSqlRawAsync(sql);
            string before = await CatalogueImageAsync(context);
            await transaction.CreateSavepointAsync("before_global_read");
            await context.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {global}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresEmailRetentionReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
            await transaction.RollbackToSavepointAsync("before_global_read");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresEmailRetentionReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(await CatalogueImageAsync(context)).IsEqualTo(before);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresEmailRetentionReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(await CatalogueImageAsync(context)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task ReadinessRequiresConfiguredGlobalAndExplicitEnrollment()
    {
        await using var migration = OpenMigrator();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresEmailRetentionReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresEmailRetentionReadPermissions.RequireReadyAsync(global, null));
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        runtime.BindTenantLease();
        await runtime.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresEmailRetentionReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment));
        await using var selected = await ((IMixedSettingsDbContextFactory)GlobalFactory())
            .CreateMixedSettingsAsync(Guid.CreateVersion7(), default);
        await selected.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresEmailRetentionReadPermissions.RequireReadyAsync(selected, fixture.NativeQueueEnrollment));
    }

    [Test]
    public async Task ProfileReapplicationPreservesOriginalCallerTransactionAndConnector()
    {
        await using var strategy = OpenMigrator();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(strategy, fixture.NativeQueueEnrollment);
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var connector = RelationalConnectionIdentity.Capture(context);
            string before = await CatalogueImageAsync(context);
            await PostgresEmailRetentionReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await PostgresEmailRetentionReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(connector.Matches(context)).IsTrue();
            await Assert.That(connector.Owns(transaction.GetDbTransaction())).IsTrue();
            await Assert.That(await CatalogueImageAsync(context)).IsEqualTo(before);
            await transaction.RollbackAsync();
        });
    }

    private static (Type Type, string Property) PrivateSource(string source) => source switch
    {
        "subject" => (typeof(EmailDispatchOutbox), nameof(EmailDispatchOutbox.Subject)),
        "plain-body" => (typeof(EmailDispatchOutbox), nameof(EmailDispatchOutbox.PlainTextBody)),
        "html-body" => (typeof(EmailDispatchOutbox), nameof(EmailDispatchOutbox.HtmlBody)),
        "recipient" => (typeof(EmailDispatchOutbox), nameof(EmailDispatchOutbox.RecipientEmail)),
        "attempt" => (typeof(EmailDispatchAttempt), nameof(EmailDispatchAttempt.Id)),
        "receipt" => (typeof(EmailDispatchReceipt), nameof(EmailDispatchReceipt.Id)),
        "delivery" => (typeof(NotificationDelivery), nameof(NotificationDelivery.Id)),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private static string PrivateGrant(HumanPrincipalSourceSql names, string source, string login)
    {
        var (type, property) = PrivateSource(source);
        return $"GRANT SELECT ({names.Column(type, property)}) ON {names.Table(type)} TO {login}";
    }

    private static async Task<string> CatalogueImageAsync(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        Type[] types = [typeof(EmailDispatchOutbox), typeof(EmailDispatchAttempt), typeof(EmailDispatchReceipt), typeof(NotificationDelivery)];
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
        command.Parameters.Add(new NpgsqlParameter("tables", types.Select(names.Table).ToArray()));
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
