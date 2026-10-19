using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Installs the approved three-column Global navigation read. Selection and membership decisions remain in C#.</summary>
public static class PostgresUserTenantNavigationReadPermissions
{
    private static readonly string[] Properties =
        [nameof(User.Id), nameof(User.IsDeleted), nameof(User.LastActiveTenantId)];

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var (schema, table, columns) = Mapping(context);
        var names = context.GetService<ISqlGenerationHelper>();
        string global = Text(enrollment.GlobalLogin);
        string predicate = ReadPredicate(context, global, requireComplete: false);
        string complete = ReadPredicate(context, global, requireComplete: true);
        string boundary = RuntimeBoundary(context, enrollment);
        string sql = $"""
            DO $navigation_permissions$ BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none', session_user::text) THEN
                    RAISE EXCEPTION 'navigation_installer_wrong_identity' USING ERRCODE = '42501';
                END IF;
                IF NOT ({NoObsoleteInstallation(schema)}) THEN
                    RAISE EXCEPTION 'navigation_requires_fresh_development_cluster' USING ERRCODE = '55000';
                END IF;
                IF NOT ({predicate}) OR NOT ({boundary})
                    OR (pg_catalog.has_any_column_privilege({global}, {Text(table)}, 'SELECT') AND NOT ({complete})) THEN
                    RAISE EXCEPTION 'navigation_read_permissions_drift' USING ERRCODE = '55000';
                END IF;
            END $navigation_permissions$;
            GRANT USAGE ON SCHEMA {names.DelimitIdentifier(schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
            GRANT SELECT ({string.Join(", ", columns.Select(names.DelimitIdentifier))})
                ON TABLE {table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
            DO $navigation_permissions$ BEGIN
                IF NOT ({complete}) OR NOT ({boundary}) THEN
                    RAISE EXCEPTION 'navigation_read_permissions_drift' USING ERRCODE = '55000';
                END IF;
            END $navigation_permissions$;
            """;
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("navigation_enrollment_required");
        enrollment.Validate();
        // SQL boolean terms need not run left-to-right: reject role substitution before resolving protected relations.
        string identity = $"""
            SELECT session_user = {Text(enrollment.GlobalLogin)} AND current_user = session_user
                AND pg_catalog.current_setting('role') IN ('none', session_user::text) AS "Value"
            """;
        if (!await context.Database.SqlQueryRaw<bool>(identity).SingleAsync(cancellationToken))
            throw new UnauthorizedAccessException("navigation_read_permissions_not_ready");
        string command = $"""
            SELECT session_user = {Text(enrollment.GlobalLogin)} AND current_user = session_user
                AND pg_catalog.current_setting('role') IN ('none', session_user::text)
                AND ({NoObsoleteInstallation(Mapping(context).Schema)})
                AND ({RuntimeBoundary(context, enrollment)})
                AND ({ReadPredicate(context, "session_user", requireComplete: true)}) AS "Value"
            """;
        if (!await context.Database.SqlQueryRaw<bool>(command).SingleAsync(cancellationToken))
            throw new UnauthorizedAccessException("navigation_read_permissions_not_ready");
    }

    // The remaining preference writer recognizes this fixed inventory, never live ACLs as authority.
    internal static string AllowedGlobalReadPredicate(ExploreDbContext context, string login) =>
        ReadPredicate(context, Text(login), requireComplete: false);

    private static string NoObsoleteInstallation(string schema) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
            JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = {Text(schema)} AND p.proname = 'user_tenant_navigation_read')
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN (
            'event_user_navigation_owner', 'event_user_navigation_global', 'event_user_navigation_migrator'))
        """;

    private static string RuntimeBoundary(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string runtime = Text(enrollment.RuntimeLogin);
        string global = Text(enrollment.GlobalLogin);
        string relation = Text(Mapping(context).Table);
        return $"""
            NOT pg_catalog.pg_has_role({runtime}, {global}, 'MEMBER')
            AND NOT pg_catalog.pg_has_role({global}, {runtime}, 'MEMBER')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role({runtime}, r.oid, 'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR r.oid = (SELECT relowner FROM pg_catalog.pg_class WHERE oid = pg_catalog.to_regclass({relation}))
                        OR pg_catalog.has_table_privilege(r.oid, {relation}, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            WHERE a.attrelid = pg_catalog.to_regclass({relation}) AND a.attnum <> 0 AND NOT a.attisdropped
                                AND pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'SELECT,INSERT,UPDATE,REFERENCES'))))
            """;
    }

    private static string ReadPredicate(ExploreDbContext context, string login, bool requireComplete)
    {
        var (schema, table, columns) = Mapping(context);
        string relation = Text(table);
        string allowed = "ARRAY[" + string.Join(",", columns.Select(Text)) + "]::text[]";
        string complete = requireComplete ? $"""
            AND NOT EXISTS (SELECT 1 FROM unnest({allowed}) AS expected(name)
                WHERE NOT pg_catalog.has_column_privilege({login}, {relation}, expected.name, 'SELECT')
                    OR NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_attribute a
                        CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                        WHERE a.attrelid = pg_catalog.to_regclass({relation}) AND a.attname = expected.name
                            AND acl.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = {login})
                            AND acl.privilege_type = 'SELECT' AND NOT acl.is_grantable))
            """ : "";
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {login} AND rolcanlogin)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role({login}, r.oid, 'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR pg_catalog.has_schema_privilege(r.oid, {Text(schema)}, 'CREATE')
                        OR pg_catalog.has_table_privilege(r.oid, {relation}, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR pg_catalog.has_any_column_privilege(r.oid, {relation}, 'INSERT,UPDATE,REFERENCES')
                        OR (r.rolname <> {login} AND pg_catalog.has_any_column_privilege(r.oid, {relation}, 'SELECT'))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            WHERE a.attrelid = pg_catalog.to_regclass({relation}) AND a.attnum <> 0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'SELECT WITH GRANT OPTION')
                                    OR (NOT (a.attname = ANY({allowed}))
                                        AND pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'SELECT'))))))
            {complete}
            """;
    }

    private static (string Schema, string Table, string[] Columns) Mapping(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(User))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(mapping.Name, schema);
        return (schema, table, Properties.Select(property => entity.FindProperty(property)!.GetColumnName(mapping)!).ToArray());
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
