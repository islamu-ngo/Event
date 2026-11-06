using System.Data;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

public static partial class PostgresSetupLiveStoragePermissions
{
    private const string SettingsDrift = "trusted_setup_settings_storage_not_ready";
    private static readonly string[] SettingProperties =
    [
        "Id", "SettingKey", "Value", "SettingValueTypeId", "IsLocked", "AllowedValues",
        "Description", "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
    ];
    private static readonly string[] SettingUpdates =
        ["Value", "SettingValueTypeId", "IsLocked", "AllowedValues", "Description", "Category", "DisplayOrder", "UpdatedAt", "UpdatedBy"];
    private static readonly (string Operation, char Command)[] SettingOperations =
        [("SELECT", 'r'), ("INSERT", 'a'), ("UPDATE", 'w')];

    internal static string GlobalSettingsCataloguePredicate(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        GlobalSettingsCataloguePredicate(context, enrollment.GlobalLogin, enrollment.MigratorLogin);

    internal static string GlobalSettingsCataloguePredicate(
        ExploreDbContext context, string globalLogin, string migratorLogin) =>
        SettingsCatalogue(context, globalLogin, migratorLogin, complete: true);

    /// <summary>Installs only fixed data permissions after the existing native source owners are declared.</summary>
    public static async Task ApplyTrustedSettingsAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var original = RelationalConnectionIdentity.Capture(context);
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change) => interrupted = true;
        void RequireOriginal()
        {
            if (interrupted || !original.Matches(context) || original.State != ConnectionState.Open
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || !original.Owns(transaction.GetDbTransaction()))
                throw new UnauthorizedAccessException(SettingsDrift);
        }
        original.StateChange += Changed;
        string savepoint = "trusted_settings_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                string migrator = SettingText(enrollment.MigratorLogin);
                if (await SettingsScalar(context, $"SELECT session_user={migrator} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)", cancellationToken) is not true)
                    throw new UnauthorizedAccessException(SettingsDrift);
                var (table, columns) = SettingsSource(context);
                bool complete = await SettingsScalar(context,
                    $"SELECT {GlobalSettingsCataloguePredicate(context, enrollment)}", cancellationToken) is true;
                RequireOriginal();
                if (!complete)
                {
                    if (await SettingsScalar(context, $"SELECT {SettingsCatalogue(context, enrollment.GlobalLogin, enrollment.MigratorLogin, complete: false)}", cancellationToken) is not true)
                        throw new InvalidOperationException(SettingsDrift);
                    var names = context.GetService<ISqlGenerationHelper>();
                    string role = names.DelimitIdentifier(enrollment.GlobalLogin);
                    string all = string.Join(',', SettingProperties.Select(property => names.DelimitIdentifier(columns[property])));
                    string updates = string.Join(',', SettingUpdates.Select(property => names.DelimitIdentifier(columns[property])));
                    bool preserved = await SettingsScalar(context,
                        $"SELECT {PostgresLocalIdentitySystemSettingPolicies.Complete(table, enrollment.MigratorLogin)}", cancellationToken) is true;
                    if (!preserved)
                        await PostgresLocalIdentitySystemSettingPolicies.InstallAsync(context, table, enrollment.MigratorLogin, cancellationToken);
                    RequireOriginal();
                    await context.Database.ExecuteSqlRawAsync(
                        $"GRANT {names.DelimitIdentifier(PostgresDiscoveryDisclosureContract.RuntimeRole)} TO {role} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                        cancellationToken);
                    await context.Database.ExecuteSqlRawAsync(
                        $"GRANT SELECT ({all}), INSERT ({all}), UPDATE ({updates}) ON {table} TO {role}; ALTER TABLE {table} ENABLE ROW LEVEL SECURITY; ALTER TABLE {table} FORCE ROW LEVEL SECURITY",
                        cancellationToken);
                    foreach (var operation in SettingOperations)
                    foreach (bool restrictive in new[] { false, true })
                    {
                        string condition = SettingsCondition(context, enrollment.GlobalLogin);
                        string policy = names.DelimitIdentifier(SettingsPolicyName(operation.Operation, restrictive));
                        string clause = (operation.Operation != "INSERT" ? $"USING ({condition}) " : "")
                            + (operation.Operation != "SELECT" ? $"WITH CHECK ({condition})" : "");
                        await context.Database.ExecuteSqlRawAsync(
                            $"CREATE POLICY {policy} ON {table} AS {(restrictive ? "RESTRICTIVE" : "PERMISSIVE")} FOR {operation.Operation} TO {role} {clause}", cancellationToken);
                        string stamp = SettingsStamp(table, enrollment.GlobalLogin, operation.Operation, restrictive);
                        await context.Database.ExecuteSqlRawAsync($"""
                            DO $setup_policy$
                            DECLARE fingerprint text;
                            BEGIN
                                SELECT {SettingText(stamp)} || COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'')
                                INTO STRICT fingerprint FROM pg_catalog.pg_policy p
                                WHERE p.polrelid=pg_catalog.to_regclass({SettingText(table)})
                                    AND p.polname={SettingText(SettingsPolicyName(operation.Operation, restrictive))};
                                EXECUTE pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',
                                    {SettingText(SettingsPolicyName(operation.Operation, restrictive))},{SettingText(table)},fingerprint);
                            END $setup_policy$;
                            """, cancellationToken);
                        RequireOriginal();
                    }
                    if (await SettingsScalar(context, $"SELECT {GlobalSettingsCataloguePredicate(context, enrollment)}", cancellationToken) is not true)
                        throw new InvalidOperationException(SettingsDrift);
                }
                RequireOriginal();
            }
            catch
            {
                RequireOriginal();
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            finally
            {
                RequireOriginal();
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }
            if (owned is not null) await owned.CommitAsync(cancellationToken);
        }
        finally { original.StateChange -= Changed; }
    }

    public static async Task RequireTrustedSettingsCurrentAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException(SettingsDrift);
        var original = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change) => interrupted = true;
        void RequireOriginal()
        {
            context.ValidateTenantLease();
            if (interrupted || original.State != ConnectionState.Open || !original.Matches(context)
                || context.TenantFilterTenantId is not null
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !original.Owns(transaction))
                throw new UnauthorizedAccessException(SettingsDrift);
        }
        original.StateChange += Changed;
        try
        {
            RequireOriginal();
            if (await SettingsScalar(context,
                $"SELECT ({SettingsSession(enrollment.GlobalLogin)}) AND ({GlobalSettingsCataloguePredicate(context, enrollment)}) AND ({GlobalCompletionCataloguePredicate(context, enrollment)})",
                cancellationToken) is not true)
                throw new UnauthorizedAccessException(SettingsDrift);
            RequireOriginal();
        }
        finally { original.StateChange -= Changed; }
    }

    private static string SettingsCatalogue(ExploreDbContext context, string globalLogin, string migratorLogin, bool complete)
    {
        var (table, columns) = SettingsSource(context);
        var entity = context.Model.FindEntityType(typeof(SystemSetting))!;
        string shape = string.Join(" AND ", SettingProperties.Select(property => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid
                AND a.attname={SettingText(columns[property])} AND a.attnum>0 AND NOT a.attisdropped
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={SettingText(entity.FindProperty(property)!.GetRelationalTypeMapping().StoreType)}
                AND a.attnotnull={(entity.FindProperty(property)!.IsNullable ? "false" : "true")} AND a.attgenerated='')
            """));
        string global = SettingText(globalLogin);
        string expected = "VALUES " + string.Join(',', SettingProperties.SelectMany(property =>
            (SettingUpdates.Contains(property, StringComparer.Ordinal) ? new[] { "SELECT", "INSERT", "UPDATE" } : ["SELECT", "INSERT"])
            .Select(privilege => $"({SettingText(columns[property])},{SettingText(privilege)})")));
        string policies = string.Join(" OR ", SettingOperations.SelectMany(operation =>
            new[] { false, true }.Select(restrictive => "(" + SettingsPolicyPredicate(context, globalLogin, operation.Operation, operation.Command, restrictive) + ")")));
        string required = string.Join(" AND ", SettingOperations.SelectMany(operation =>
            new[] { false, true }.Select(restrictive => "EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE "
                + SettingsPolicyPredicate(context, globalLogin, operation.Operation, operation.Command, restrictive) + ")")));
        string rights = complete ? $"""
            AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=t.oid AND a.attname=e.column_name AND acl.privilege_type=e.privilege
                    AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})
                    AND acl.grantor=t.relowner AND NOT acl.is_grantable))
            """ : "";
        return $"""
            (WITH expected(column_name,privilege) AS ({expected}),
            members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid=pg_catalog.to_regclass({SettingText(table)})
                AND t.relkind='r' AND t.relpersistence='p' AND NOT t.relispartition
                AND t.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={SettingText(migratorLogin)})
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                AND (SELECT count(*)=13 FROM pg_catalog.pg_attribute WHERE attrelid=t.oid AND attnum>0 AND NOT attisdropped)
                AND ({shape})
                AND NOT EXISTS (SELECT 1 FROM members r WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole
                    OR r.rolreplication OR r.oid=t.relowner
                    OR pg_catalog.has_schema_privilege(r.oid,t.relnamespace,'CREATE')
                    OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl WHERE acl.grantee=0)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl WHERE a.attrelid=t.oid AND acl.grantee=0)
                AND ({(complete ? SettingsDisclosureBinding(globalLogin, migratorLogin) : $"(NOT pg_catalog.pg_has_role({global},{SettingText(PostgresDiscoveryDisclosureContract.RuntimeRole)},'MEMBER') OR ({SettingsDisclosureBinding(globalLogin, migratorLogin)}))")})
                {rights}
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN members r
                    CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                    WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                        AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                            OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege)
                                AND NOT ({(complete ? $"r.rolname={global} AND a.attnum>0 AND EXISTS (SELECT 1 FROM expected e WHERE e.column_name=a.attname AND e.privilege=permission.privilege)" : "false")}))))
                AND ({PostgresLocalIdentitySystemSettingPolicies.CompleteExistingSources("t.oid")})
                AND {(complete ? $"t.relrowsecurity AND t.relforcerowsecurity AND ({required}) AND ({PostgresLocalIdentitySystemSettingPolicies.Complete(table, migratorLogin)})" : $"((NOT t.relrowsecurity AND NOT t.relforcerowsecurity AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND NOT ({PostgresLocalIdentitySystemSettingPolicies.ExistingSourcePolicyPredicate("t.oid")}))) OR (t.relrowsecurity AND t.relforcerowsecurity AND ({PostgresLocalIdentitySystemSettingPolicies.Complete(table, migratorLogin)})))")}
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid
                    AND (0::oid=ANY(p.polroles) OR EXISTS (SELECT 1 FROM members r WHERE r.oid=ANY(p.polroles))
                        OR p.polname LIKE 'trusted_setup_settings_%')
                    AND NOT ({(complete ? policies : "false")}))))
            """;
    }

    private static string SettingsPolicyPredicate(ExploreDbContext context, string login, string operation, char command, bool restrictive)
    {
        var (table, columns) = SettingsSource(context);
        string key = $"pg_catalog.quote_ident({SettingText(columns["SettingKey"])})";
        string comparisons = string.Join(" || ' OR ' || ", TrustedSetupSettingsWriter.Keys.Select(value =>
            $"'(convert_to((' || {key} || ')::text, ''UTF8''::name) = convert_to(' || pg_catalog.quote_literal({SettingText(value)}) || '::text, ''UTF8''::name))'"));
        string expression = $"""
            '((SESSION_USER = CURRENT_USER) AND (CURRENT_USER = ' || pg_catalog.quote_literal({SettingText(login)})
            || '::name) AND (NULLIF(current_setting(''app.current_tenant_id''::text, true), ''''::text) IS NULL) AND ('
            || {comparisons} || '))'
            """;
        return $"""
            p.polrelid=pg_catalog.to_regclass({SettingText(table)}) AND p.polname={SettingText(SettingsPolicyName(operation, restrictive))}
            AND p.polcmd='{command}' AND p.polpermissive={(restrictive ? "false" : "true")}
            AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={SettingText(login)})]
            AND {(operation == "INSERT" ? "p.polqual IS NULL" : $"pg_catalog.pg_get_expr(p.polqual,p.polrelid)=({expression})")}
            AND {(operation == "SELECT" ? "p.polwithcheck IS NULL" : $"pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)=({expression})")}
            AND pg_catalog.obj_description(p.oid,'pg_policy')={SettingText(SettingsStamp(table, login, operation, restrictive))}
                || COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend d JOIN pg_catalog.pg_proc f ON f.oid=d.refobjid
                JOIN pg_catalog.pg_namespace n ON n.oid=f.pronamespace WHERE d.classid='pg_catalog.pg_policy'::regclass
                    AND d.objid=p.oid AND d.refclassid='pg_catalog.pg_proc'::regclass AND n.nspname<>'pg_catalog')
            """;
    }

    private static string SettingsCondition(ExploreDbContext context, string login)
    {
        var (_, columns) = SettingsSource(context);
        string key = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(columns["SettingKey"]);
        return SettingsSession(login) + " AND (" + string.Join(" OR ", TrustedSetupSettingsWriter.Keys.Select(value =>
            $"pg_catalog.convert_to({key}::text,'UTF8'::name)=pg_catalog.convert_to({SettingText(value)}::text,'UTF8'::name)")) + ")";
    }

    private static string SettingsSession(string login) =>
        $"SESSION_USER=CURRENT_USER AND CURRENT_USER={SettingText(login)}::name AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL";

    private static string SettingsDisclosureBinding(string global, string migrator) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m JOIN pg_catalog.pg_roles executor ON executor.oid=m.roleid
            WHERE executor.rolname={SettingText(PostgresDiscoveryDisclosureContract.RuntimeRole)} AND NOT executor.rolcanlogin
                AND m.member=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={SettingText(global)})
                AND m.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={SettingText(migrator)})
                AND m.inherit_option AND NOT m.set_option AND NOT m.admin_option)
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m JOIN pg_catalog.pg_roles executor ON executor.oid=m.roleid
            WHERE executor.rolname={SettingText(PostgresDiscoveryDisclosureContract.RuntimeRole)}
                AND m.member=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={SettingText(global)})
                AND (m.grantor<>(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={SettingText(migrator)})
                    OR NOT m.inherit_option OR m.set_option OR m.admin_option))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m JOIN pg_catalog.pg_roles executor ON executor.oid=m.roleid
            WHERE executor.rolname={SettingText(PostgresDiscoveryDisclosureContract.RuntimeRole)}
                AND m.admin_option AND pg_catalog.pg_has_role({SettingText(global)},m.member,'MEMBER'))
        AND pg_catalog.pg_has_role({SettingText(global)},{SettingText(PostgresDiscoveryDisclosureContract.RuntimeRole)},'USAGE')
        AND NOT pg_catalog.pg_has_role({SettingText(global)},{SettingText(PostgresDiscoveryDisclosureContract.RuntimeRole)},'SET')
        """;
    private static string SettingsPolicyName(string operation, bool restrictive) =>
        "trusted_setup_settings_" + operation.ToLowerInvariant() + (restrictive ? "_restriction" : "");
    private static string SettingsStamp(string table, string login, string operation, bool restrictive) =>
        "trusted-setup-settings:v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{table}\n{login}\n{operation}\n{restrictive}\n{string.Join('\n', TrustedSetupSettingsWriter.Keys)}"))) + ":";
    private static string SettingText(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static (string Table, Dictionary<string, string> Columns) SettingsSource(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(SystemSetting))!;
        if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(SettingProperties.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException(SettingsDrift);
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        return (context.GetService<ISqlGenerationHelper>().DelimitIdentifier(store.Name, schema),
            SettingProperties.ToDictionary(property => property, property => entity.FindProperty(property)!.GetColumnName(store)!, StringComparer.Ordinal));
    }

    private static async Task<object?> SettingsScalar(ExploreDbContext context, string sql, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(token);
    }
}
