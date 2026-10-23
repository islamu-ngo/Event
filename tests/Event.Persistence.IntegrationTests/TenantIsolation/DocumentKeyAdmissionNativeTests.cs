using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

/// <summary>Tests the database credential boundary, not fabricated human creator authority.</summary>
[ClassDataSource<DocumentKeyAdmissionNativeTests.DocumentKeyFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class DocumentKeyAdmissionNativeTests(DocumentKeyAdmissionNativeTests.DocumentKeyFixture fixture)
{
    [Test]
    [Arguments(SettingsDocumentKeys.Tenant.EventDefaults, false)]
    [Arguments(SettingsDocumentKeys.Tenant.EventDefaults, true)]
    [Arguments("tenant.undeclared_admission_probe", false)]
    [Arguments("tenant.undeclared_admission_probe", true)]
    [Arguments(SettingsDocumentKeys.Tenant.Branding, true)]
    [Arguments(SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity, true)]
    public async Task GlobalDocumentInsertRejectsUnapprovedKeyDespiteAttackerSelectedSession(
        string documentKey, bool selectTenant)
    {
        await using var observer = fixture.OpenMigrator();
        await observer.Database.OpenConnectionAsync();
        await fixture.RequireSeedAsync(observer);
        string before = await GraphDigestAsync(observer, fixture.TenantId);
        var factory = new GlobalExploreDbContextFactory(fixture.GlobalOptions());
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireSessionAsync(global, fixture.Enrollment.GlobalLogin, "");
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            global, fixture.Enrollment, IdentityDatabaseTopology.Colocated);

        var names = new HumanPrincipalSourceSql(global);
        string table = names.Table(typeof(TenantSettingsDocument));
        await using (var privileges = Command(global, """
            SELECT count(*)=11 AND bool_and(pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,'INSERT'))
            FROM pg_catalog.pg_attribute a
            WHERE a.attrelid=CAST(@table AS regclass) AND a.attnum>0 AND NOT a.attisdropped
            """))
        {
            privileges.Parameters.AddWithValue("table", table);
            await Assert.That(await privileges.ExecuteScalarAsync()).IsEqualTo(true);
        }

        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var lease = global.ContextId;
        await using (var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var physical = transaction.GetDbTransaction();
            try
            {
                string tenantSetting = selectTenant ? fixture.TenantId.ToString() : "";
                // A stolen database credential can set its GUC; the EF lease stays sealed-null.
                await using (var session = Command(global, "SELECT pg_catalog.set_config('app.current_tenant_id',@tenant,true)"))
                {
                    session.Parameters.AddWithValue("tenant", tenantSetting);
                    await Assert.That(await session.ExecuteScalarAsync()).IsEqualTo(tenantSetting);
                }
                await RequireSessionAsync(global, fixture.Enrollment.GlobalLogin, tenantSetting);
                await using var insert = DocumentInsert(global, fixture.TenantId, documentKey);

                var denied = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
                await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(denied.MessageText.Contains("row-level security", StringComparison.Ordinal)).IsTrue();
                await Assert.That(global.ContextId).IsEqualTo(lease);
                await Assert.That(global.TenantFilterTenantId).IsNull();
                await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
                await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
                await Assert.That(physical.Connection).IsSameReferenceAs(connector);
                await Assert.That(connector.ProcessID).IsEqualTo(backend);
                await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await fixture.RequireSeedAsync(observer);
                await Assert.That(await GraphDigestAsync(observer, fixture.TenantId)).IsEqualTo(before);
            }
        }
        await RequireSessionAsync(global, fixture.Enrollment.GlobalLogin, "");
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
    }

    [Test]
    [Arguments(SettingsDocumentKeys.Tenant.Branding)]
    [Arguments(SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity)]
    public async Task SealedNullGlobalCanInsertOnlyAnAbsentApprovedDocumentAndRollBack(string documentKey)
    {
        await using var observer = fixture.OpenMigrator();
        await observer.Database.OpenConnectionAsync();
        await fixture.RequireSeedAsync(observer);
        string before = await GraphDigestAsync(observer, fixture.TenantId);
        var factory = new GlobalExploreDbContextFactory(fixture.GlobalOptions());
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.Enrollment, IdentityDatabaseTopology.Colocated);
        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        var contextId = global.ContextId;
        int backend = connector.ProcessID;
        await using (var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var physical = transaction.GetDbTransaction();
            try
            {
                await RequireSessionAsync(global, fixture.Enrollment.GlobalLogin, "");
                // This exercises the explicitly accepted credential capability, not a human receipt.
                await using var insert = DocumentInsert(global, fixture.TenantId, documentKey);
                await Assert.That(await insert.ExecuteNonQueryAsync()).IsEqualTo(1);
                global.RequireBoundTenantLease();
                await Assert.That(global.TenantFilterTenantId).IsNull();
                await Assert.That(global.ContextId).IsEqualTo(contextId);
                await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
                await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
                await Assert.That(physical.Connection).IsSameReferenceAs(connector);
                await Assert.That(connector.ProcessID).IsEqualTo(backend);
                await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
                await Assert.That(await GraphDigestAsync(observer, fixture.TenantId)).IsEqualTo(before);
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await fixture.RequireSeedAsync(observer);
                await Assert.That(await GraphDigestAsync(observer, fixture.TenantId)).IsEqualTo(before);
            }
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.Enrollment, IdentityDatabaseTopology.Colocated);
        await RequireSessionAsync(global, fixture.Enrollment.GlobalLogin, "");
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("permissive")]
    [Arguments("unrestricted")]
    public async Task ChangedDocumentRestrictionRejectsReadinessAndInstallationWithoutRepair(string drift)
    {
        const string policyName = "tenant_creation_document_keys";
        await using var migrator = fixture.OpenMigrator();
        await migrator.Database.OpenConnectionAsync();
        await fixture.RequireSeedAsync(migrator);
        string before = await GraphDigestAsync(migrator, fixture.TenantId);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(TenantSettingsDocument));
        string policy = names.Quote(policyName);
        string login = names.Quote(fixture.Enrollment.GlobalLogin);
        string expression;
        string comment;
        await using (var original = Command(migrator, """
            SELECT pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid), pg_catalog.obj_description(p.oid,'pg_policy')
            FROM pg_catalog.pg_policy p WHERE p.polrelid=CAST(@table AS regclass) AND p.polname=@policy
                AND NOT p.polpermissive AND p.polcmd='a' AND p.polqual IS NULL
                AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@login)]
            """))
        {
            original.Parameters.AddWithValue("table", table);
            original.Parameters.AddWithValue("policy", policyName);
            original.Parameters.AddWithValue("login", fixture.Enrollment.GlobalLogin);
            await using var reader = await original.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            expression = reader.GetString(0);
            comment = reader.GetString(1);
            await Assert.That(await reader.ReadAsync()).IsFalse();
        }
        string restore = $"""
            DROP POLICY IF EXISTS {policy} ON {table};
            CREATE POLICY {policy} ON {table} AS RESTRICTIVE FOR INSERT TO {login} WITH CHECK ({expression});
            COMMENT ON POLICY {policy} ON {table} IS {SqlLiteral(comment)};
            """;
        var factory = new GlobalExploreDbContextFactory(fixture.GlobalOptions());
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.Enrollment, IdentityDatabaseTopology.Colocated);
        var connector = (NpgsqlConnection)global.Database.GetDbConnection();
        var contextId = global.ContextId;
        int backend = connector.ProcessID;
        try
        {
            string mutation = drift switch
            {
                "missing" => $"DROP POLICY {policy} ON {table};",
                "permissive" => $"""
                    DROP POLICY {policy} ON {table};
                    CREATE POLICY {policy} ON {table} AS PERMISSIVE FOR INSERT TO {login} WITH CHECK ({expression});
                    COMMENT ON POLICY {policy} ON {table} IS {SqlLiteral(comment)};
                    """,
                "unrestricted" => $"ALTER POLICY {policy} ON {table} WITH CHECK (true);",
                _ => throw new ArgumentOutOfRangeException(nameof(drift))
            };
            await using (var change = await migrator.Database.BeginTransactionAsync())
            {
                await using var command = Command(migrator, mutation);
                await command.ExecuteNonQueryAsync();
                if (drift == "unrestricted")
                {
                    // A privileged writer can update the mutable hash comment too;
                    // rejection must still depend on the exact compiled expression.
                    await using var hash = Command(migrator, """
                        SELECT pg_catalog.md5(p.polwithcheck::text) FROM pg_catalog.pg_policy p
                        WHERE p.polrelid=CAST(@table AS regclass) AND p.polname=@policy
                        """);
                    hash.Parameters.AddWithValue("table", table);
                    hash.Parameters.AddWithValue("policy", policyName);
                    string changedHash = (string)(await hash.ExecuteScalarAsync()
                        ?? throw new InvalidOperationException("The changed policy digest is missing."));
                    string matchingComment = comment[..(comment.LastIndexOf(':') + 1)] + changedHash;
                    await using var restamp = Command(migrator,
                        $"COMMENT ON POLICY {policy} ON {table} IS {SqlLiteral(matchingComment)};");
                    await restamp.ExecuteNonQueryAsync();
                }
                await change.CommitAsync();
            }
            string changed = await DocumentPolicyImageAsync(migrator, table, policyName);
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.Enrollment, IdentityDatabaseTopology.Colocated));
            await Assert.That(denied.Message).IsEqualTo("tenant_creation_permissions_not_ready");
            var installation = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresTenantCreationPermissions.ApplyAsync(migrator, fixture.Enrollment, IdentityDatabaseTopology.Colocated));
            await Assert.That(installation.Message).IsEqualTo("tenant_creation_permissions_drift");
            await Assert.That(await DocumentPolicyImageAsync(migrator, table, policyName)).IsEqualTo(changed);
            global.RequireBoundTenantLease();
            await Assert.That(global.ContextId).IsEqualTo(contextId);
            await Assert.That(global.TenantFilterTenantId).IsNull();
            await Assert.That(global.Database.CurrentTransaction).IsNull();
            await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await Assert.That(await GraphDigestAsync(migrator, fixture.TenantId)).IsEqualTo(before);
        }
        finally
        {
            // Restore only the captured exact policy; no protocol reinstall or grant repair.
            await using var restoration = await migrator.Database.BeginTransactionAsync(CancellationToken.None);
            await using var command = Command(migrator, restore);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            await restoration.CommitAsync(CancellationToken.None);
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.Enrollment, IdentityDatabaseTopology.Colocated);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
        await fixture.RequireSeedAsync(migrator);
        await Assert.That(await GraphDigestAsync(migrator, fixture.TenantId)).IsEqualTo(before);
    }

    private static async Task<string> DocumentPolicyImageAsync(ExploreDbContext context, string table, string policy)
    {
        await using var command = Command(context, """
            SELECT COALESCE((SELECT pg_catalog.md5((pg_catalog.to_jsonb(p)
                || pg_catalog.jsonb_build_object('comment',pg_catalog.obj_description(p.oid,'pg_policy')))::text)
                FROM pg_catalog.pg_policy p WHERE p.polrelid=CAST(@table AS regclass) AND p.polname=@policy),'absent')
            """);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("policy", policy);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The document policy observation is missing."));
    }

    private static NpgsqlCommand DocumentInsert(ExploreDbContext context, Guid tenantId, string documentKey)
    {
        var names = new HumanPrincipalSourceSql(context);
        string[] properties =
        [
            nameof(TenantSettingsDocument.Id), nameof(TenantSettingsDocument.TenantId),
            nameof(TenantSettingsDocument.DocumentKey), nameof(TenantSettingsDocument.SchemaVersion),
            nameof(TenantSettingsDocument.DefaultsVersion), nameof(TenantSettingsDocument.PayloadJson),
            nameof(TenantSettingsDocument.ConcurrencyStamp), nameof(TenantSettingsDocument.CreatedAt),
            nameof(TenantSettingsDocument.CreatedBy), nameof(TenantSettingsDocument.UpdatedAt),
            nameof(TenantSettingsDocument.UpdatedBy)
        ];
        string columns = string.Join(",", properties.Select(property => names.Column(typeof(TenantSettingsDocument), property)));
        var insert = Command(context, $"""
            INSERT INTO {names.Table(typeof(TenantSettingsDocument))} ({columns})
            VALUES (@id,@tenant,@key,1,@defaults,@payload,@stamp,@created,NULL,NULL,NULL)
            """);
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("tenant", tenantId);
        insert.Parameters.AddWithValue("key", documentKey);
        insert.Parameters.AddWithValue("defaults", "admission-probe-v1");
        insert.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, "{}");
        insert.Parameters.AddWithValue("stamp", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("created", DateTime.UtcNow);
        return insert;
    }

    private static string SqlLiteral(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private static async Task RequireSessionAsync(ExploreDbContext context, string login, string tenant)
    {
        await using var command = Command(context, """
            SELECT current_user=session_user AND session_user=@login
                AND COALESCE(pg_catalog.current_setting('app.current_tenant_id',true),'')=@tenant
            """);
        command.Parameters.AddWithValue("login", login);
        command.Parameters.AddWithValue("tenant", tenant);
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    private static async Task<string> GraphDigestAsync(ExploreDbContext context, Guid tenantId)
    {
        var names = new HumanPrincipalSourceSql(context);
        Type[] types = [typeof(Tenant), typeof(TenantSettingsDocument), typeof(EventDiscoveryRevision), typeof(TenantUser), typeof(TenantUserRoleGrant)];
        string[] projections = types.Select(type =>
            $"(SELECT pg_catalog.md5(COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r) ORDER BY r.{names.Column(type, "Id")})::text,'[]')) FROM {names.Table(type)} r WHERE r.{names.Column(type, type == typeof(Tenant) ? "Id" : "TenantId")}=@tenant)").ToArray();
        await using var command = Command(context, "SELECT " + string.Join(" || ':' || ", projections));
        command.Parameters.AddWithValue("tenant", tenantId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The independent graph observation is missing."));
    }

    private static NpgsqlCommand Command(ExploreDbContext context, string sql) => new(sql,
        (NpgsqlConnection)context.Database.GetDbConnection(),
        (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());

    /// <summary>Seeds an existing parent before creator grants, using the public Migrator EF lifecycle.</summary>
    public sealed class DocumentKeyFixture : IAsyncInitializer, IAsyncDisposable
    {
        private readonly PostgreSqlContainerFixture _database = new();
        private readonly object _disposal = new();
        private Task? _disposeTask;

        public PostgresNativeQueueEnrollment Enrollment => _database.NativeQueueEnrollment;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public PrimaryDatabaseConnectionOptions GlobalOptions() => _database.GetNativeQueueOptions(PrimaryDatabaseRole.Global);

        public async Task InitializeAsync()
        {
            await _database.InitializeAsync();
            await using var strategyContext = OpenMigrator();
            await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var seed = OpenMigrator();
                await using var gate = new EventDiscoveryEnrollmentGate(seed);
                await using var gateLease = await gate.AcquireAsync();
                await using var transaction = await seed.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var connector = (NpgsqlConnection)seed.Database.GetDbConnection();
                int backend = connector.ProcessID;
                var physical = transaction.GetDbTransaction();
                var lease = seed.ContextId;
                await RequireSessionAsync(seed, Enrollment.MigratorLogin, "");
                seed.Tenants.Add(new Tenant
                {
                    Id = TenantId,
                    FullName = "Document admission parent",
                    Slug = "document-admission-" + TenantId.ToString("N"),
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!,
                    CreatedAt = DateTime.UtcNow
                });
                seed.Set<EventDiscoveryRevision>().Add(new EventDiscoveryRevision
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = TenantId,
                    IdentityEpoch = 0,
                    DisclosureEpoch = 1
                });
                await seed.SaveChangesAsync();
                gate.RequireCurrent();
                await Assert.That(seed.ContextId).IsEqualTo(lease);
                await Assert.That(seed.TenantFilterTenantId).IsNull();
                await Assert.That(seed.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
                await Assert.That(physical.Connection).IsSameReferenceAs(connector);
                await Assert.That(connector.ProcessID).IsEqualTo(backend);
                // ConfigureApplication's transaction interceptor flushes disclosure before COMMIT.
                await transaction.CommitAsync();
                gate.RequireCurrent();
                await Assert.That(connector.ProcessID).IsEqualTo(backend);
            });

            await using var install = OpenMigrator();
            await install.Database.OpenConnectionAsync();
            await RequireSeedAsync(install);
            await using var installation = await install.Database.BeginTransactionAsync();
            await RequireSessionAsync(install, Enrollment.MigratorLogin, "");
            await PostgresTenantCreationPermissions.ApplyAsync(
                install, Enrollment, IdentityDatabaseTopology.Colocated);
            await installation.CommitAsync();
        }

        public ExploreDbContext OpenMigrator()
        {
            var builder = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, _database.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
            var context = new ExploreDbContext(builder.Options);
            context.BindTenantLease();
            return context;
        }

        public async Task RequireSeedAsync(ExploreDbContext observer)
        {
            var names = new HumanPrincipalSourceSql(observer);
            await using var command = Command(observer, $"""
                SELECT (SELECT count(*) FROM {names.Table(typeof(Tenant))} WHERE {names.Column(typeof(Tenant), nameof(Tenant.Id))}=@tenant)=1
                    AND (SELECT count(*) FROM {names.Table(typeof(EventDiscoveryRevision))}
                        WHERE {names.Column(typeof(EventDiscoveryRevision), nameof(EventDiscoveryRevision.TenantId))}=@tenant
                            AND {names.Column(typeof(EventDiscoveryRevision), nameof(EventDiscoveryRevision.IdentityEpoch))}=0
                            AND {names.Column(typeof(EventDiscoveryRevision), nameof(EventDiscoveryRevision.DisclosureEpoch))}=1)=1
                    AND NOT EXISTS (SELECT 1 FROM {names.Table(typeof(TenantSettingsDocument))}
                        WHERE {names.Column(typeof(TenantSettingsDocument), nameof(TenantSettingsDocument.TenantId))}=@tenant)
                """);
            command.Parameters.AddWithValue("tenant", TenantId);
            await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
        }

        public ValueTask DisposeAsync()
        {
            lock (_disposal)
                return new ValueTask(_disposeTask ??= _database.DisposeAsync().AsTask());
        }
    }
}
