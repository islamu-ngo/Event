using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>Fixed storage permissions for the nonsecret provisioning policy projection.</summary>
public static class PostgresProvisioningPolicyPermissions
{
    private const string Drift = "provisioning_policy_permissions_drift";
    private static readonly string[] Properties = ["Id", "SettingKey", "IsPresent", "IsLocked", "Value", "Revision"];
    private static readonly string[] Mutable = ["IsPresent", "IsLocked", "Value", "Revision"];
    private static readonly string[] EmailMutable = ["IsPresent", "IsLocked", "Revision"];

    /// <summary>Fixed mapped Global rights for this projection only; no live ACL-derived inventory.</summary>
    internal static bool TryGetGlobalColumns(ExploreDbContext context, Type type,
        out IReadOnlyList<string> readColumns, out IReadOnlyList<string> updateColumns)
    {
        if (type != typeof(ManagedTenantProvisioningPolicySetting))
        {
            readColumns = [];
            updateColumns = [];
            return false;
        }
        var source = Map(context);
        readColumns = Array.AsReadOnly(Properties.Select(property => source.Columns[property]).ToArray());
        updateColumns = Array.AsReadOnly(Mutable.Select(property => source.Columns[property]).ToArray());
        return true;
    }

    /// <summary>Retains this source owner's complete RLS, role, column and policy proof for composed readers.</summary>
    internal static string CompleteCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Manifest(Map(context), enrollment, complete: true);

    /// <summary>Installs a wholly absent manifest or verifies it unchanged, on the original installation transaction.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var source = Map(context);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var physicalTransaction = transaction.GetDbTransaction();
        var contextId = context.ContextId;
        int backend = connection.ProcessID;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs args) => interrupted = true;
        void RequireOriginal()
        {
            if (interrupted || context.ContextId != contextId || !ReferenceEquals(connection, context.Database.GetDbConnection())
                || connection.State != ConnectionState.Open || connection.ProcessID != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || !ReferenceEquals(physicalTransaction, context.Database.CurrentTransaction?.GetDbTransaction()))
                throw new InvalidOperationException("provisioning_policy_installation_owner_changed");
        }
        async Task<object?> ReadAsync(string sql)
        {
            RequireOriginal();
            object? value = await ScalarAsync(context, sql, cancellationToken);
            RequireOriginal();
            return value;
        }
        async Task RunAsync(string sql)
        {
            RequireOriginal();
            await ExecuteAsync(context, sql, cancellationToken);
            RequireOriginal();
        }
        string savepoint = "provisioning_permissions_" + Guid.CreateVersion7().ToString("N");
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        connection.StateChange += Changed;
        try
        {
            if (await ReadAsync($"SELECT {Identity(enrollment.MigratorLogin)}") is not true)
                throw new UnauthorizedAccessException("provisioning_policy_installer_wrong_identity");
            bool complete = await ReadAsync("SELECT " + Manifest(source, enrollment, true)) is true;
            if (!complete)
            {
                if (await ReadAsync($"SELECT ({Manifest(source, enrollment, false)}) AND ({Fresh(source)})") is not true)
                    throw new InvalidOperationException(Drift);
                foreach (string login in new[] { enrollment.GlobalLogin, enrollment.EmailInstanceMutationLogin })
                {
                    await RunAsync($"GRANT USAGE ON SCHEMA {Quote(source.Schema)} TO {Quote(login)}");
                    await RunAsync($"GRANT SELECT({Columns(source, Properties)}) ON {source.Table} TO {Quote(login)}");
                    string[] updates = login == enrollment.GlobalLogin ? Mutable : EmailMutable;
                    await RunAsync($"GRANT UPDATE({Columns(source, updates)}) ON {source.Table} TO {Quote(login)}");
                }
                await RunAsync($"ALTER TABLE {source.Table} ENABLE ROW LEVEL SECURITY; ALTER TABLE {source.Table} FORCE ROW LEVEL SECURITY");
                foreach (var policy in Policies(source, enrollment))
                {
                    string usingClause = policy.Command == "INSERT" ? "" : $" USING ({policy.Condition})";
                    string checkClause = policy.Command == "SELECT" ? "" : $" WITH CHECK ({policy.Condition})";
                    await RunAsync($"CREATE POLICY {Quote(policy.Name)} ON {source.Table} FOR {policy.Command} TO {Quote(policy.Login)}{usingClause}{checkClause}");
                    string expressionHash = await ReadAsync($"""
                        SELECT COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'')
                        FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                            AND p.polname={Text(policy.Name)}
                        """) as string ?? throw new InvalidOperationException(Drift);
                    await RunAsync($"COMMENT ON POLICY {Quote(policy.Name)} ON {source.Table} IS {Text(Stamp(source, policy) + expressionHash)}");
                }
                if (await ReadAsync("SELECT " + Manifest(source, enrollment, true)) is not true)
                    throw new InvalidOperationException(Drift);
            }
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }
        finally
        {
            try { await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None); }
            finally { connection.StateChange -= Changed; }
        }
        if (owned is not null)
        {
            RequireOriginal();
            await owned.CommitAsync(cancellationToken);
        }
    }

    /// <summary>Attests an already open, tenantless Global or instance-email owner without acquiring or replacing it.</summary>
    public static async Task RequireCurrentAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("provisioning_policy_enrollment_required");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        if (context.Database.GetDbConnection() is not NpgsqlConnection connection || connection.State != ConnectionState.Open)
            throw new UnauthorizedAccessException("provisioning_policy_open_owner_required");
        var contextId = context.ContextId;
        var tenant = context.TenantContext;
        var transaction = context.Database.CurrentTransaction;
        var physicalTransaction = transaction?.GetDbTransaction();
        int backend = connection.ProcessID;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs args) => interrupted = true;
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (interrupted || context.ContextId != contextId || !ReferenceEquals(tenant, context.TenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || System.Transactions.Transaction.Current is not null
                || !ReferenceEquals(connection, context.Database.GetDbConnection())
                || connection.State != ConnectionState.Open || connection.ProcessID != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || !ReferenceEquals(physicalTransaction, context.Database.CurrentTransaction?.GetDbTransaction()))
                throw new UnauthorizedAccessException("provisioning_policy_original_owner_required");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            // Identity is evaluated separately: role substitution must not reach relation-dependent catalog probes.
            string identity = $"({Identity(enrollment.GlobalLogin)}) OR ({Identity(enrollment.EmailInstanceMutationLogin)})";
            if (await ScalarAsync(context, $"SELECT ({identity}) AND {NullTenant}", cancellationToken) is not true)
                throw new UnauthorizedAccessException("provisioning_policy_current_owner_not_ready");
            RequireOriginal();
            if (await ScalarAsync(context, "SELECT " + Manifest(Map(context), enrollment, true), cancellationToken) is not true)
                throw new UnauthorizedAccessException("provisioning_policy_current_owner_not_ready");
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }

    private static string Manifest(Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string owner = RoleOid(enrollment.MigratorLogin);
        string[] callers = [enrollment.GlobalLogin, enrollment.RuntimeLogin,
            enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin];
        string callerNames = string.Join(',', callers.Select(Text));
        string expected = string.Join(',', new[] { enrollment.GlobalLogin, enrollment.EmailInstanceMutationLogin }
            .SelectMany(login => Properties.Select(property => (Login: login, Property: property, Privilege: "SELECT"))
                .Concat((login == enrollment.GlobalLogin ? Mutable : EmailMutable)
                    .Select(property => (Login: login, Property: property, Privilege: "UPDATE"))))
            .Select(item => $"({Text(item.Login)},{Text(source.Columns[item.Property])},{Text(item.Privilege)})"));
        string columnShape = string.Join(" AND ", Properties.Select(property => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid
                AND a.attname={Text(source.Columns[property])} AND a.attnum>0 AND NOT a.attisdropped
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(source.Types[property])}
                AND a.attnotnull={(property == "Value" ? "false" : "true")} AND a.attgenerated='' AND a.attidentity='')
            """));
        string checks = string.Join(" AND ", ExpectedChecks(source).Select(check => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_constraint k WHERE k.conrelid=c.oid
                AND k.contype='c' AND k.conname={Text(check.Key)} AND k.convalidated
                AND COALESCE((pg_catalog.to_jsonb(k)->>'conenforced')::boolean,true)
                AND pg_catalog.pg_get_expr(k.conbin,k.conrelid)={Text(check.Value)})
            """));
        string required = complete ? $"""
            AND c.relrowsecurity AND c.relforcerowsecurity
            AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                SELECT 1 FROM actual a WHERE a.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=e.login)
                    AND a.column_name=e.column_name AND a.privilege_type=e.privilege))
            AND (SELECT count(*) FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid)={Policies(source, enrollment).Length}
            AND {string.Join(" AND ", Policies(source, enrollment).Select(policy => $"EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid AND ({PolicyPredicate(source, policy)}))"))}
            AND pg_catalog.has_schema_privilege({RoleOid(enrollment.GlobalLogin)},{Text(source.Schema)},'USAGE')
            AND pg_catalog.has_schema_privilege({RoleOid(enrollment.EmailInstanceMutationLogin)},{Text(source.Schema)},'USAGE')
            """ : "";
        return $"""
            (WITH expected(login,column_name,privilege) AS (VALUES {expected}),
            actual AS ({Actual(source)})
            SELECT EXISTS (
                SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)})
                AND c.relkind='r' AND c.relpersistence='p' AND c.relowner={owner}
                AND (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ({Text(enrollment.MigratorLogin)},{callerNames}) AND rolcanlogin)=5
                AND (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped)=6
                AND {columnShape}
                AND {checks}
                AND (SELECT count(*) FROM pg_catalog.pg_constraint k WHERE k.conrelid=c.oid AND k.contype='c')=3
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint k WHERE k.conrelid=c.oid AND k.contype IN ('f','x'))
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    JOIN pg_catalog.pg_collation col ON col.oid=a.attcollation
                    JOIN pg_catalog.pg_namespace ns ON ns.oid=col.collnamespace
                    WHERE a.attrelid=c.oid AND a.attname={Text(source.Columns["SettingKey"])}
                        AND col.collname='C' AND ns.nspname='pg_catalog')
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_attribute a ON a.attrelid=i.indrelid AND a.attname={Text(source.Columns["Id"])}
                    WHERE i.indrelid=c.oid AND i.indisprimary AND i.indisvalid AND i.indnatts=1 AND i.indkey[0]=a.attnum)
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_attribute a ON a.attrelid=i.indrelid AND a.attname={Text(source.Columns["SettingKey"])}
                    WHERE i.indrelid=c.oid AND i.indisunique AND i.indisvalid AND i.indnatts=1 AND i.indkey[0]=a.attnum AND i.indpred IS NULL AND i.indexprs IS NULL)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=c.oid OR inhparent=c.oid)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger WHERE tgrelid=c.oid AND NOT tgisinternal)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=c.oid)
                AND NOT EXISTS (SELECT 1 FROM actual a WHERE a.is_grantable OR a.grantor<>{owner}
                    OR NOT EXISTS (SELECT 1 FROM expected e WHERE a.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=e.login)
                        AND a.column_name=e.column_name AND a.privilege_type=e.privilege))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                    WHERE roleid IN ({RoleOid(enrollment.GlobalLogin)},{RoleOid(enrollment.EmailInstanceMutationLogin)}))
                AND NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_roles principal CROSS JOIN pg_catalog.pg_roles member
                    WHERE principal.rolname IN ({callerNames}) AND pg_catalog.pg_has_role(principal.oid,member.oid,'MEMBER')
                    AND (member.oid=c.relowner OR member.rolsuper OR member.rolbypassrls OR member.rolcreaterole
                        OR member.rolcreatedb OR member.rolreplication
                        OR (member.rolname IN ({callerNames}) AND member.oid<>principal.oid)
                        OR pg_catalog.has_schema_privilege(member.oid,{Text(source.Schema)},'CREATE')
                        OR pg_catalog.has_table_privilege(member.oid,c.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR CASE WHEN current_setting('server_version_num')::int>=170000
                            THEN pg_catalog.has_table_privilege(member.oid,c.oid,'MAINTAIN') ELSE false END
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum<0
                            AND pg_catalog.has_column_privilege(member.oid,c.oid,a.attnum,'SELECT,INSERT,UPDATE,REFERENCES'))))
                {required}))
            """;
    }

    private static string Actual(Source source) => $"""
        SELECT NULL::text AS column_name,acl.* FROM pg_catalog.pg_class c
        CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(c.relacl,pg_catalog.acldefault('r',c.relowner))) acl
        WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)}) AND acl.grantee<>c.relowner
        UNION ALL
        SELECT a.attname::text,acl.* FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_attribute a ON a.attrelid=c.oid
        CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
        WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attnum<>0 AND NOT a.attisdropped AND acl.grantee<>c.relowner
        """;

    private static string Fresh(Source source) => $"""
        NOT EXISTS ({Actual(source)})
        AND EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)})
            AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity)
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({Text(source.Table)}))
        """;

    private static Dictionary<string, string> ExpectedChecks(Source source)
    {
        // pg_get_expr's canonical forms for these three fixed model checks; never normalize away literal bytes.
        string revision = source.Columns["Revision"], present = source.Columns["IsPresent"];
        string locked = source.Columns["IsLocked"], value = source.Columns["Value"], key = source.Columns["SettingKey"];
        string values = string.Join(", ", ManagedTenantProvisioningPolicyInventory.ValueKeys
            .Select(item => "'" + item.Replace("'", "''", StringComparison.Ordinal) + "'::character varying"));
        return new()
        {
            ["ck_provisioning_policy_revision"] = $"({revision} >= 0)",
            ["ck_provisioning_policy_absence"] = $"(({present} = true) OR (({locked} = false) AND ({value} IS NULL)))",
            ["ck_provisioning_policy_value"] = $"(({value} IS NULL) OR (({key})::text = ANY ((ARRAY[{values}])::text[])))"
        };
    }

    private static Policy[] Policies(Source source, PostgresNativeQueueEnrollment enrollment)
    {
        string[] emailKeys = EmailDeliverySettingKeys.All
            .Intersect(ManagedTenantProvisioningPolicyInventory.Keys, StringComparer.Ordinal)
            .Except(ManagedTenantProvisioningPolicyInventory.ValueKeys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        string email = $"{Identity(enrollment.EmailInstanceMutationLogin)} AND {NullTenant} AND {Quote(source.Columns["SettingKey"])} IN ({string.Join(',', emailKeys.Select(Text))})";
        string global = $"{Identity(enrollment.GlobalLogin)} AND {NullTenant}";
        string migrator = Identity(enrollment.MigratorLogin);
        return [
            new("provisioning_policy_global_read", enrollment.GlobalLogin, "SELECT", "r", global),
            new("provisioning_policy_global_update", enrollment.GlobalLogin, "UPDATE", "w", global),
            new("provisioning_policy_email_read", enrollment.EmailInstanceMutationLogin, "SELECT", "r", email),
            new("provisioning_policy_email_update", enrollment.EmailInstanceMutationLogin, "UPDATE", "w", email),
            new("provisioning_policy_migrator_read", enrollment.MigratorLogin, "SELECT", "r", migrator),
            new("provisioning_policy_migrator_insert", enrollment.MigratorLogin, "INSERT", "a", migrator),
            new("provisioning_policy_migrator_update", enrollment.MigratorLogin, "UPDATE", "w", migrator)
        ];
    }

    private static string PolicyPredicate(Source source, Policy policy) => $"""
        p.polname={Text(policy.Name)} AND p.polcmd={Text(policy.Code)} AND p.polpermissive
        AND p.polroles=ARRAY[{RoleOid(policy.Login)}]
        AND {(policy.Command == "INSERT" ? "p.polqual IS NULL" : "p.polqual IS NOT NULL")}
        AND {(policy.Command == "SELECT" ? "p.polwithcheck IS NULL" : "p.polwithcheck IS NOT NULL")}
        AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(Stamp(source, policy))}
            || COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'')
        """;

    private static string Stamp(Source source, Policy policy) => "event-provisioning-policy:v1:"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{source.Table}\n{policy.Name}\n{policy.Login}\n{policy.Command}\n{policy.Condition}"))) + ":";

    private static Source Map(ExploreDbContext context)
    {
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ManagedTenantProvisioningPolicySetting))
            ?? throw new InvalidOperationException("provisioning_policy_model_missing");
        if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(Properties.Order(StringComparer.Ordinal))
            || entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.GetNavigations().Any() || entity.GetSkipNavigations().Any()
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id"
            || !entity.FindProperty("Revision")!.IsConcurrencyToken
            || entity.FindProperty("SettingKey")!.GetMaxLength() != 256
            || entity.FindProperty("SettingKey")!.GetCollation() != "C"
            || entity.GetProperties().Any(property => property.IsNullable != (property.Name == "Value"))
            || entity.GetProperties().Any(property => property.ClrType != (property.Name switch
            {
                "Id" => typeof(Guid),
                "IsPresent" or "IsLocked" => typeof(bool),
                "Revision" => typeof(long),
                _ => typeof(string)
            })))
            throw new InvalidOperationException("provisioning_policy_model_shape_drift");
        string name = entity.GetTableName()!;
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        var store = StoreObjectIdentifier.Table(name, entity.GetSchema());
        string[] columnNames = Properties.Select(property => entity.FindProperty(property)!.GetColumnName(store)!).ToArray();
        if (!columnNames.SequenceEqual(new[] { "id", "setting_key", "is_present", "is_locked", "value", "revision" }, StringComparer.Ordinal))
            throw new InvalidOperationException("provisioning_policy_model_column_drift");
        return new(schema, Quote(schema) + "." + Quote(name),
            Properties.ToDictionary(property => property, property => entity.FindProperty(property)!.GetColumnName(store)!),
            Properties.ToDictionary(property => property, property => entity.FindProperty(property)!.GetRelationalTypeMapping().StoreType));
    }

    private const string NullTenant = "NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL";
    private static string Identity(string login) => $"""
        session_user={Text(login)} AND current_user=session_user
        AND pg_catalog.current_setting('role') IN ('none',session_user::text)
        AND pg_catalog.current_setting('session_replication_role')='origin'
        """;
    private static string RoleOid(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Columns(Source source, IEnumerable<string> properties) =>
        string.Join(',', properties.Select(property => Quote(source.Columns[property])));
    private static string Quote(string identifier)
    {
        using var builder = new NpgsqlCommandBuilder();
        return builder.QuoteIdentifier(identifier);
    }
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static DbCommand Command(ExploreDbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.CommandText = sql;
        return command;
    }
    private static async Task<object?> ScalarAsync(ExploreDbContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(context, sql);
        return await command.ExecuteScalarAsync(cancellationToken);
    }
    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(context, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private sealed record Source(string Schema, string Table, Dictionary<string, string> Columns, Dictionary<string, string> Types);
    private sealed record Policy(string Name, string Login, string Command, string Code, string Condition);
}
