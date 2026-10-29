using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

/// <summary>Real storage admission only; no worker claim or business graph is fabricated.</summary>
[NotInParallel]
public sealed partial class ManagedTenantWorkerCreationPermissionsNativeTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task OriginalMigratorInstallsWorkerManifestAndGlobalReattestsWithoutEffects(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = new WorkerFixture(topology);
        await fixture.InitializeAsync();
        await using var migrator = OpenMigrator(fixture);
        await migrator.Database.OpenConnectionAsync();
        await RequireSessionAsync(migrator, fixture.NativeQueueEnrollment.MigratorLogin);
        await EnsurePolicyProjectionAsync(migrator);
        await PostgresProvisioningPolicyPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        await PostgresTenantCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, topology);
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await RequireSessionAsync(global, fixture.NativeQueueEnrollment.GlobalLogin);
        await PostgresProvisioningPolicyPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment);
        await PostgresTenantCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology);
        string rowsBefore = await RowCountsAsync(migrator);
        string runtimeBefore = await RuntimeGrantsAsync(migrator, fixture.NativeQueueEnrollment.RuntimeLogin);
        var globalConnection = (NpgsqlConnection)global.Database.GetDbConnection();
        int globalBackend = globalConnection.ProcessID;
        var globalLease = global.ContextId;
        var missing = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology));
        await Assert.That(missing.Message).IsEqualTo("managed_tenant_worker_creation_permissions_not_ready");
        await Assert.That(await RowCountsAsync(migrator)).IsEqualTo(rowsBefore);

        await using (var installation = await migrator.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var connector = (NpgsqlConnection)migrator.Database.GetDbConnection();
            var physical = installation.GetDbTransaction();
            int backend = connector.ProcessID;
            // The first RED must occur here, after genuine identity/core controls.
            await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, topology);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(installation);
            await Assert.That(installation.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(physical.Connection).IsSameReferenceAs(connector);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await installation.CommitAsync();
        }

        string complete = await CatalogueImageAsync(migrator);
        await using (var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var physical = transaction.GetDbTransaction();
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology);
            var unenrolled = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, null, topology));
            await Assert.That(unenrolled.Message).IsEqualTo("managed_tenant_worker_creation_permissions_not_ready");
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology);
            global.RequireBoundTenantLease();
            await Assert.That(global.ContextId).IsEqualTo(globalLease);
            await Assert.That(global.TenantFilterTenantId).IsNull();
            await Assert.That(global.IsTenantFilterBypassed).IsFalse();
            await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(globalConnection);
            await Assert.That(globalConnection.ProcessID).IsEqualTo(globalBackend);
            await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(physical.Connection).IsSameReferenceAs(globalConnection);
            await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using (var repeat = await migrator.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, topology);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(repeat);
            await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(complete);
            await repeat.RollbackAsync(CancellationToken.None);
        }
        await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(complete);
        await Assert.That(await RowCountsAsync(migrator)).IsEqualTo(rowsBefore);
        await Assert.That(await RuntimeGrantsAsync(migrator, fixture.NativeQueueEnrollment.RuntimeLogin)).IsEqualTo(runtimeBefore);
        await RequireSessionAsync(global, fixture.NativeQueueEnrollment.GlobalLogin);
        await Assert.That(globalConnection.ProcessID).IsEqualTo(globalBackend);
        await Assert.That(global.Database.CurrentTransaction).IsNull();

        var names = new HumanPrincipalSourceSql(migrator);
        var identifiers = migrator.GetService<ISqlGenerationHelper>();
        string globalRole = identifiers.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
        string user = names.Table(typeof(User));
        string emailVerified = identifiers.DelimitIdentifier(names.ColumnName(typeof(User), nameof(User.EmailVerified)));
        string pii = names.Table(typeof(UserPii));
        string email = identifiers.DelimitIdentifier(names.ColumnName(typeof(UserPii), nameof(UserPii.Email)));
        await RequireDriftRejectedAsync($"REVOKE UPDATE ({emailVerified}) ON {user} FROM {globalRole}",
            $"GRANT UPDATE ({emailVerified}) ON {user} TO {globalRole}");
        await RequireDriftRejectedAsync($"GRANT SELECT ({email}) ON {pii} TO {globalRole}",
            $"REVOKE SELECT ({email}) ON {pii} FROM {globalRole}");
        foreach (string property in new[] { "_dateOfBirth", "_birthDateCollectionReceiptId", "_birthDateRevision" })
        {
            string column = identifiers.DelimitIdentifier(names.ColumnName(typeof(UserPii), property));
            await RequireDriftRejectedAsync($"GRANT SELECT ({column}) ON {pii} TO {globalRole}",
                $"REVOKE SELECT ({column}) ON {pii} FROM {globalRole}");
            await RequireDriftRejectedAsync($"GRANT UPDATE ({column}) ON {pii} TO {globalRole}",
                $"REVOKE UPDATE ({column}) ON {pii} FROM {globalRole}");
        }
        await RequireDriftRejectedAsync($"GRANT SELECT (ctid) ON {user} TO {globalRole}",
            $"REVOKE SELECT (ctid) ON {user} FROM {globalRole}");
        await RequireDriftRejectedAsync($"GRANT UPDATE ({emailVerified}) ON {user} TO {globalRole} WITH GRANT OPTION",
            $"REVOKE GRANT OPTION FOR UPDATE ({emailVerified}) ON {user} FROM {globalRole}");
        await RequireDriftRejectedAsync($"GRANT SELECT ({email}) ON {pii} TO PUBLIC",
            $"REVOKE SELECT ({email}) ON {pii} FROM PUBLIC");
        string operation = names.Table(typeof(ManagedTenantProvisioningOperation));
        string delivery = identifiers.DelimitIdentifier(names.ColumnName(typeof(ManagedTenantProvisioningOperation),
            nameof(ManagedTenantProvisioningOperation.CurrentOutboxMessageId)));
        await RequireDriftRejectedAsync($"REVOKE INSERT ({delivery}) ON {operation} FROM {globalRole}",
            $"GRANT INSERT ({delivery}) ON {operation} TO {globalRole}");
        string outbox = names.Table(typeof(OutboxMessage));
        string payload = identifiers.DelimitIdentifier(names.ColumnName(typeof(OutboxMessage), nameof(OutboxMessage.Payload)));
        await RequireDriftRejectedAsync($"GRANT SELECT ({payload}) ON {outbox} TO {globalRole}",
            $"REVOKE SELECT ({payload}) ON {outbox} FROM {globalRole}");
        string policy = names.Table(typeof(ManagedTenantProvisioningPolicySetting));
        string policyValue = identifiers.DelimitIdentifier(names.ColumnName(
            typeof(ManagedTenantProvisioningPolicySetting), nameof(ManagedTenantProvisioningPolicySetting.Value)));
        string policyId = identifiers.DelimitIdentifier(names.ColumnName(
            typeof(ManagedTenantProvisioningPolicySetting), nameof(ManagedTenantProvisioningPolicySetting.Id)));
        await RequireDriftRejectedAsync($"REVOKE UPDATE ({policyValue}) ON {policy} FROM {globalRole}",
            $"GRANT UPDATE ({policyValue}) ON {policy} TO {globalRole}");
        await RequireDriftRejectedAsync($"GRANT UPDATE ({policyId}) ON {policy} TO {globalRole}",
            $"REVOKE UPDATE ({policyId}) ON {policy} FROM {globalRole}");
        await RequireDriftRejectedAsync($"ALTER TABLE {policy} NO FORCE ROW LEVEL SECURITY",
            $"ALTER TABLE {policy} FORCE ROW LEVEL SECURITY");
        string settings = names.Table(typeof(TenantSetting));
        string capture = names.Name("email_delivery_policy_controls_capture");
        string originalCapture;
        await using (var original = Command(migrator, "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure(@routine))"))
        {
            original.Parameters.AddWithValue("routine", capture + "()");
            originalCapture = (string)(await original.ExecuteScalarAsync())!;
        }
        await RequireCaptureRejectedAsync($"ALTER TABLE {settings} DISABLE TRIGGER email_policy_control_custody",
            $"ALTER TABLE {settings} ENABLE TRIGGER email_policy_control_custody");
        await RequireCaptureRejectedAsync($"""
            CREATE OR REPLACE FUNCTION {capture}() RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
                SET search_path = pg_catalog AS $capture_test$ BEGIN RETURN NEW; END $capture_test$
            """, originalCapture);
        await RequireCaptureRejectedAsync($"""
            DROP TRIGGER email_policy_control_custody ON {settings};
            CREATE TRIGGER email_policy_control_custody BEFORE INSERT OR UPDATE OR DELETE ON {settings}
                FOR EACH ROW EXECUTE FUNCTION {capture}('instance')
            """, $"""
            DROP TRIGGER email_policy_control_custody ON {settings};
            CREATE TRIGGER email_policy_control_custody BEFORE INSERT OR UPDATE OR DELETE ON {settings}
                FOR EACH ROW EXECUTE FUNCTION {capture}('tenant')
            """);
        await RequireRehashedEmailPolicyRejectedAsync("provisioning_policy_email_read", update: false);
        await RequireRehashedEmailPolicyRejectedAsync("provisioning_policy_email_update", update: true);
        await using (var generated = Command(migrator, """
            SELECT NOT pg_catalog.has_column_privilege(@login, @table, 'xmin', 'INSERT')
                AND NOT pg_catalog.has_column_privilege(@login, @table, 'xmin', 'UPDATE')
            """))
        {
            generated.Parameters.AddWithValue("login", fixture.NativeQueueEnrollment.GlobalLogin);
            generated.Parameters.AddWithValue("table", operation);
            await Assert.That(await generated.ExecuteScalarAsync()).IsEqualTo(true);
        }

        await using (var transaction = await global.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var selectTenant = Command(global, "SELECT pg_catalog.set_config('app.current_tenant_id',@tenant,true)");
            selectTenant.Parameters.AddWithValue("tenant", Guid.CreateVersion7().ToString());
            await selectTenant.ExecuteScalarAsync();
            var rejection = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology));
            await Assert.That(rejection.Message).IsEqualTo("managed_tenant_worker_creation_permissions_not_ready");
            await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(globalConnection.ProcessID).IsEqualTo(globalBackend);
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology);
        await PostgresProvisioningPolicyPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment);
        await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(complete);
        await Assert.That(await RowCountsAsync(migrator)).IsEqualTo(rowsBefore);
        await Assert.That(await RuntimeGrantsAsync(migrator, fixture.NativeQueueEnrollment.RuntimeLogin)).IsEqualTo(runtimeBefore);

        async Task RequireDriftRejectedAsync(string attack, string restore)
        {
            await using (var mutation = Command(migrator, attack))
                await mutation.ExecuteNonQueryAsync();
            try
            {
                string attacked = await CatalogueImageAsync(migrator);
                var rejection = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology));
                await Assert.That(rejection.Message).IsEqualTo("managed_tenant_worker_creation_permissions_not_ready");
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, topology));
                await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(attacked);
                await Assert.That(await RowCountsAsync(migrator)).IsEqualTo(rowsBefore);
                await Assert.That(global.Database.GetDbConnection()).IsSameReferenceAs(globalConnection);
                await Assert.That(globalConnection.ProcessID).IsEqualTo(globalBackend);
                await Assert.That(global.ContextId).IsEqualTo(globalLease);
                await Assert.That(global.Database.CurrentTransaction).IsNull();
            }
            finally
            {
                await using var cleanup = Command(migrator, restore);
                await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
            }
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology);
            await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(complete);
        }

        async Task RequireCaptureRejectedAsync(string attack, string restore)
        {
            await using (var mutation = Command(migrator, attack))
                await mutation.ExecuteNonQueryAsync();
            try
            {
                string attacked = await CaptureImageAsync();
                var rejected = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology));
                await Assert.That(rejected.Message).IsEqualTo("managed_tenant_worker_creation_permissions_not_ready:storage:TenantSetting");
                var installer = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, topology));
                await Assert.That(installer.Message).IsEqualTo(rejected.Message);
                await Assert.That(await CaptureImageAsync()).IsEqualTo(attacked);
                await Assert.That(await RowCountsAsync(migrator)).IsEqualTo(rowsBefore);
            }
            finally
            {
                await using var cleanup = Command(migrator, restore);
                await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
            }
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology);
        }

        async Task<string> CaptureImageAsync()
        {
            await using var command = Command(migrator, """
                SELECT jsonb_build_array(t.tgenabled,t.tgtype,t.tgnargs,encode(t.tgargs,'hex'),
                    pg_catalog.pg_get_functiondef(t.tgfoid))::text
                FROM pg_catalog.pg_trigger t
                WHERE t.tgrelid=pg_catalog.to_regclass(@table) AND t.tgname='email_policy_control_custody'
                """);
            command.Parameters.AddWithValue("table", settings);
            return (string)(await command.ExecuteScalarAsync())!;
        }

        async Task RequireRehashedEmailPolicyRejectedAsync(string policyName, bool update)
        {
            await PostgresProvisioningPolicyPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment);
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology);
            string originalUsing, originalComment;
            string? originalCheck;
            await using (var original = Command(migrator, """
                SELECT pg_catalog.pg_get_expr(polqual,polrelid), pg_catalog.pg_get_expr(polwithcheck,polrelid),
                    pg_catalog.obj_description(oid,'pg_policy')
                FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass(@table) AND polname=@policy
                """))
            {
                original.Parameters.AddWithValue("table", policy);
                original.Parameters.AddWithValue("policy", policyName);
                await using var reader = await original.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new InvalidOperationException("The genuine email policy is absent.");
                originalUsing = reader.GetString(0);
                originalCheck = reader.IsDBNull(1) ? null : reader.GetString(1);
                originalComment = reader.GetString(2);
            }
            string[] fingerprint = originalComment.Split(':');
            await Assert.That(fingerprint.Length).IsEqualTo(5);
            string prefix = string.Join(":", fingerprint.Take(3)) + ":";
            string quotedPolicy = identifiers.DelimitIdentifier(policyName);
            string identityOnly = $"""
                session_user={PolicyLiteral(fixture.NativeQueueEnrollment.EmailInstanceMutationLogin)}
                AND current_user=session_user
                AND pg_catalog.current_setting('role') IN ('none',session_user::text)
                AND pg_catalog.current_setting('session_replication_role')='origin'
                AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL
                """;
            try
            {
                await using (var alter = Command(migrator, $"ALTER POLICY {quotedPolicy} ON {policy} USING ({identityOnly})" +
                    (update ? $" WITH CHECK ({identityOnly})" : "")))
                    await alter.ExecuteNonQueryAsync();
                string actualHash;
                await using (var hash = Command(migrator, """
                    SELECT COALESCE(pg_catalog.md5(polqual::text),'') || ':' || COALESCE(pg_catalog.md5(polwithcheck::text),'')
                    FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass(@table) AND polname=@policy
                    """))
                {
                    hash.Parameters.AddWithValue("table", policy);
                    hash.Parameters.AddWithValue("policy", policyName);
                    actualHash = (string)(await hash.ExecuteScalarAsync()
                        ?? throw new InvalidOperationException("The altered email policy is absent."));
                }
                await using (var rehash = Command(migrator,
                    $"COMMENT ON POLICY {quotedPolicy} ON {policy} IS {PolicyLiteral(prefix + actualHash)}"))
                    await rehash.ExecuteNonQueryAsync();
                string attacked = await CatalogueImageAsync(migrator);
                await Assert.That(attacked).IsNotEqualTo(complete);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresProvisioningPolicyPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology));
                var projectionDrift = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PostgresProvisioningPolicyPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment));
                await Assert.That(projectionDrift.Message).IsEqualTo("provisioning_policy_permissions_drift");
                var workerDrift = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, topology));
                await Assert.That(workerDrift.Message).IsEqualTo("managed_tenant_worker_creation_permissions_drift");
                await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(attacked);
                await Assert.That(await RowCountsAsync(migrator)).IsEqualTo(rowsBefore);
                await Assert.That(await RuntimeGrantsAsync(migrator, fixture.NativeQueueEnrollment.RuntimeLogin)).IsEqualTo(runtimeBefore);
            }
            finally
            {
                await using (var restore = Command(migrator, $"ALTER POLICY {quotedPolicy} ON {policy} USING ({originalUsing})" +
                    (originalCheck is null ? "" : $" WITH CHECK ({originalCheck})")))
                    await restore.ExecuteNonQueryAsync();
                await using var comment = Command(migrator,
                    $"COMMENT ON POLICY {quotedPolicy} ON {policy} IS {PolicyLiteral(originalComment)}");
                await comment.ExecuteNonQueryAsync();
            }
            await PostgresProvisioningPolicyPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment);
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment, topology);
            await Assert.That(await CatalogueImageAsync(migrator)).IsEqualTo(complete);
        }
    }

    private static string PolicyLiteral(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal) + "'";

    private static async Task RequireSessionAsync(ExploreDbContext context, string login)
    {
        await using var command = Command(context, """
            SELECT current_user=session_user AND session_user=@login
                AND current_setting('role') IN ('none',session_user::text)
                AND NULLIF(current_setting('app.current_tenant_id',true),'') IS NULL
            """);
        command.Parameters.AddWithValue("login", login);
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    private static string[] Tables(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        return ManagedTenantWorkerCreationInventory.Sources.Select(source => names.Table(source.Entity)).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static async Task EnsurePolicyProjectionAsync(ExploreDbContext migrator)
    {
        var model = migrator.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(ManagedTenantProvisioningPolicySetting))
            ?? throw new InvalidOperationException("The worker policy projection model is missing.");
        string table = entity.GetTableName()!;
        string schema = migrator.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()!.TargetSchema;
        await Assert.That(entity.GetSchema()).IsEqualTo(schema);
        var names = new HumanPrincipalSourceSql(migrator);
        await using var existing = Command(migrator, "SELECT pg_catalog.to_regclass(@table) IS NOT NULL");
        existing.Parameters.AddWithValue("table", names.Table(typeof(ManagedTenantProvisioningPolicySetting)));
        if (await existing.ExecuteScalarAsync() is true) return;

        // The unapplied model addition is isolated to its own generated table/index
        // operations; never create an identity-store table or install extra grants.
        MigrationOperation[] operations = migrator.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel())
            .Where(operation => operation is EnsureSchemaOperation ensure && ensure.Name == schema
                || operation is CreateTableOperation create && create.Name == table && create.Schema == schema
                || operation is CreateIndexOperation index && index.Table == table && index.Schema == schema)
            .ToArray();
        await Assert.That(operations.OfType<CreateTableOperation>().Count()).IsEqualTo(1);
        foreach (var command in migrator.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
            await command.ExecuteNonQueryAsync(migrator.GetService<IRelationalConnection>());
        await Assert.That(await existing.ExecuteScalarAsync()).IsEqualTo(true);
    }

    private static async Task<string> RowCountsAsync(ExploreDbContext context)
    {
        await using var command = Command(context, "SELECT " + string.Join(" || ':' || ", Tables(context)
            .Select(table => $"(SELECT count(*)::text FROM {table})")));
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Worker source counts are missing."));
    }

    private static async Task<string> CatalogueImageAsync(ExploreDbContext context)
    {
        await using var command = Command(context, """
            WITH tables AS (SELECT pg_catalog.to_regclass(name) AS id FROM unnest(@tables) name)
            SELECT pg_catalog.md5(pg_catalog.jsonb_build_object(
                'relations',(SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(c.oid,c.relowner,c.relacl,c.relrowsecurity,c.relforcerowsecurity) ORDER BY c.oid)
                    FROM pg_catalog.pg_class c JOIN tables t ON t.id=c.oid),
                'columns',(SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(a.attrelid,a.attnum,a.attacl) ORDER BY a.attrelid,a.attnum)
                    FROM pg_catalog.pg_attribute a JOIN tables t ON t.id=a.attrelid),
                'policies',(SELECT pg_catalog.jsonb_agg(pg_catalog.to_jsonb(p)||pg_catalog.jsonb_build_object('comment',pg_catalog.obj_description(p.oid,'pg_policy')) ORDER BY p.oid)
                    FROM pg_catalog.pg_policy p JOIN tables t ON t.id=p.polrelid))::text)
            """);
        command.Parameters.AddWithValue("tables", Tables(context));
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Worker catalogue image is missing."));
    }

    private static async Task<string> RuntimeGrantsAsync(ExploreDbContext context, string runtime)
    {
        await using var command = Command(context, """
            WITH tables AS (SELECT pg_catalog.to_regclass(name) AS id FROM unnest(@tables) name), grants AS (
                SELECT a.attrelid,a.attnum,acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable
                FROM pg_catalog.pg_attribute a JOIN tables t ON t.id=a.attrelid CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@runtime)
                UNION ALL
                SELECT c.oid,0,acl.grantor,acl.grantee,acl.privilege_type,acl.is_grantable
                FROM pg_catalog.pg_class c JOIN tables t ON t.id=c.oid CROSS JOIN LATERAL pg_catalog.aclexplode(c.relacl) acl
                WHERE acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@runtime))
            SELECT pg_catalog.md5(COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(g) ORDER BY g.attrelid,g.attnum,g.privilege_type)::text,'[]')) FROM grants g
            """);
        command.Parameters.AddWithValue("tables", Tables(context));
        command.Parameters.AddWithValue("runtime", runtime);
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Runtime grants are missing."));
    }

    private static ExploreDbContext OpenMigrator(PostgreSqlContainerFixture fixture)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        // Model-differ operations must target the actual fixture namespace, not
        // Migrator composition's canonical migration-snapshot namespace.
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            new RelationalNamespaceOptionsExtension(options.Schema, options.Schema));
        var context = new ExploreDbContext(builder.Options);
        context.BindTenantLease();
        return context;
    }

    private static NpgsqlCommand Command(ExploreDbContext context, string sql) => new(sql,
        (NpgsqlConnection)context.Database.GetDbConnection(),
        (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());

    private sealed class WorkerFixture(IdentityDatabaseTopology topology) : PostgreSqlContainerFixture(topology)
    {
    }
}
