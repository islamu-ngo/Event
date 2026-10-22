using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Installs fixed read-only binding columns for the configured Global login; account selection belongs to C#.</summary>
public static class PostgresExternalLoginReadPermissions
{
    private static readonly string[] Properties =
    [
        nameof(UserExternalLogin.Id), nameof(UserExternalLogin.UserId),
        nameof(UserExternalLogin.AuthenticationProviderId), nameof(UserExternalLogin.ProviderKey),
        nameof(UserExternalLogin.ProviderDisplayName), nameof(UserExternalLogin.CreatedAt),
        nameof(UserExternalLogin.CreatedBy), nameof(UserExternalLogin.UpdatedAt), nameof(UserExternalLogin.UpdatedBy)
    ];

    internal static IReadOnlyList<string> GlobalReadColumns(ExploreDbContext context) =>
        Array.AsReadOnly(Mapping(context).Columns);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var (schema, table, columns) = Mapping(context);
        var names = context.GetService<ISqlGenerationHelper>();
        string global = Text(enrollment.GlobalLogin);
        string relation = Text(table);
        string predicate = ReadPredicate(context, enrollment, global, requireComplete: false);
        string complete = ReadPredicate(context, enrollment, global, requireComplete: true);
        string boundary = RuntimeBoundary(context, enrollment);
        string legacyFunction = names.DelimitIdentifier(
            context.Model.FindEntityType(typeof(UserExternalLogin))!.GetTableName()! + "_for_authentication", schema);
        string sql = $"""
            DO $read_permissions$ BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none', session_user::text) THEN
                    RAISE EXCEPTION 'external_login_installer_wrong_identity' USING ERRCODE = '42501';
                END IF;
                IF pg_catalog.to_regprocedure({Text(legacyFunction + "(integer,text)")}) IS NOT NULL
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN (
                        'event_external_login_authentication_owner', 'event_external_login_authentication_migrator',
                        'event_external_login_authentication_global')) THEN
                    RAISE EXCEPTION 'external_login_requires_fresh_development_database' USING ERRCODE = '55000';
                END IF;
                IF NOT ({predicate}) OR NOT ({boundary})
                    OR (pg_catalog.has_any_column_privilege({global}, {relation}, 'SELECT') AND NOT ({complete})) THEN
                    RAISE EXCEPTION 'external_login_read_permissions_drift' USING ERRCODE = '55000';
                END IF;
            END $read_permissions$;
            GRANT USAGE ON SCHEMA {names.DelimitIdentifier(schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
            GRANT SELECT ({string.Join(", ", columns.Select(names.DelimitIdentifier))})
                ON TABLE {table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
            DO $read_permissions$ BEGIN
                IF NOT ({complete}) OR NOT ({boundary}) THEN
                    RAISE EXCEPTION 'external_login_read_permissions_drift' USING ERRCODE = '55000';
                END IF;
            END $read_permissions$;
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
        if (enrollment is null) throw new UnauthorizedAccessException("external_login_enrollment_required");
        enrollment.Validate();
        string command = $"""
            SELECT session_user = {Text(enrollment.GlobalLogin)} AND current_user = session_user
                AND pg_catalog.current_setting('role') IN ('none', session_user::text)
                AND ({RuntimeBoundary(context, enrollment)})
                AND ({ReadPredicate(context, enrollment, "session_user", requireComplete: true)}) AS "Value"
            """;
        if (!await context.Database.SqlQueryRaw<bool>(command).SingleAsync(cancellationToken))
            throw new UnauthorizedAccessException("external_login_read_permissions_not_ready");
    }

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
                    AND (pg_catalog.has_table_privilege(r.oid, {relation}, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            WHERE a.attrelid = pg_catalog.to_regclass({relation}) AND a.attnum <> 0 AND NOT a.attisdropped
                                AND pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'SELECT,INSERT,UPDATE,REFERENCES'))))
            """;
    }

    private static string ReadPredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, string login, bool requireComplete)
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
        string binding = $"""
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
        return $"(({binding}) OR ({PostgresManagedTenantWorkerCreationPermissions.CompleteGlobalCataloguePredicate(context, enrollment)}))";
    }

    private static (string Schema, string Table, string[] Columns) Mapping(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(UserExternalLogin))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(mapping.Name, schema);
        return (schema, table, Properties.Select(property => entity.FindProperty(property)!.GetColumnName(mapping)!).ToArray());
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
