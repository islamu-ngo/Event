using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Execute-only Global capability for one live user's navigation preference, without identity data.</summary>
public static class PostgresUserTenantNavigationContract
{
    public const string OwnerRole = "event_user_navigation_owner";
    public const string GlobalRole = "event_user_navigation_global";
    public const string MigratorRole = "event_user_navigation_migrator";
    private const string FunctionName = "user_tenant_navigation_read";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token) : null;
        var model = Build(context, enrollment);
        await using (var existing = context.Database.GetDbConnection().CreateCommand())
        {
            existing.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            existing.CommandText = $"""
                SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}','{GlobalRole}','{MigratorRole}'))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
                        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {Text(model.SchemaName)} AND p.proname = '{FunctionName}')
                """;
            if (await existing.ExecuteScalarAsync(token) is true)
            {
                // Existing enrollment is immutable. Never repair drift by replacing a routine or normalizing grants.
                await RequireReadyAsync(context, enrollment, token);
                if (owned is not null) await owned.CommitAsync(token);
                return;
            }
        }
        await context.Database.ExecuteSqlRawAsync($"""
            CREATE ROLE {OwnerRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            CREATE ROLE {GlobalRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            CREATE ROLE {MigratorRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT {MigratorRole} TO {Quote(enrollment.MigratorLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT {GlobalRole} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
            GRANT USAGE, CREATE ON SCHEMA {model.Schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {model.Schema} TO {GlobalRole};
            GRANT SELECT ({model.Id}, {model.Deleted}, {model.Preference}) ON {model.Table} TO {OwnerRole};
            DO $install$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE FUNCTION {model.Function}(p_user uuid)
                    RETURNS TABLE(user_id uuid, last_active_tenant_id uuid)
                    LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${model.Body}$body$ $ddl$;
                REVOKE ALL ON FUNCTION {model.Signature} FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION {model.Signature} TO {GlobalRole};
                PERFORM set_config('role', v_previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {model.Schema} FROM {OwnerRole};
            """, token);
        await RequireReadyAsync(context, enrollment, token);
        if (owned is not null) await owned.CommitAsync(token);
    }

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(FunctionName, Schema(context));

    internal static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token)
    {
        var model = Build(context, enrollment);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync(token);
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"DO $guard$ BEGIN {model.Guard} END; $guard$;";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = """
            SELECT p.prosrc, p.prosecdef, r.rolname,
                p.proconfig = ARRAY['search_path=pg_catalog']::text[]
                AND p.proargnames = ARRAY['p_user','user_id','last_active_tenant_id']::text[]
                AND p.proargmodes = ARRAY['i','t','t']::"char"[]
                AND p.proallargtypes = ARRAY['pg_catalog.uuid'::regtype::oid,
                    'pg_catalog.uuid'::regtype::oid,'pg_catalog.uuid'::regtype::oid]
                AND p.pronargs = 1 AND p.proargtypes[0] = 'pg_catalog.uuid'::regtype
                AND p.proargdefaults IS NULL AND p.pronargdefaults = 0 AND p.provariadic = 0
                AND p.proretset AND p.prorettype = 'pg_catalog.record'::regtype
                AND p.prokind = 'f' AND p.proparallel = 'u' AND p.provolatile = 's'
                AND NOT p.proleakproof AND NOT p.proisstrict
                AND p.procost = 100 AND p.prorows = 1000 AND p.prosupport = 0
                AND p.prosqlbody IS NULL AND p.protrftypes IS NULL
                AND p.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
            FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_roles r ON r.oid = p.proowner
            WHERE p.oid = pg_catalog.to_regprocedure(@signature)
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "signature";
        parameter.Value = model.Signature;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || reader.GetString(0) != model.Body
            || !reader.GetBoolean(1) || reader.GetString(2) != OwnerRole
            || reader.IsDBNull(3) || !reader.GetBoolean(3))
            throw new UnauthorizedAccessException("native_user_navigation_contract_drift");
    }

    private static Contract Build(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        string[] roles = [OwnerRole, GlobalRole, MigratorRole];
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(roles.Contains))
            throw new InvalidOperationException("user_navigation_enrollment_collision");
        var sql = context.GetService<ISqlGenerationHelper>();
        var user = context.Model.FindEntityType(typeof(User))!;
        string schema = Schema(context);
        string table = sql.DelimitIdentifier(user.GetTableName()!, schema);
        string ColumnName(string name) => user.FindProperty(name)!
            .GetColumnName(StoreObjectIdentifier.Table(user.GetTableName()!, user.GetSchema()))!;
        string id = sql.DelimitIdentifier(ColumnName(nameof(User.Id)));
        string deleted = sql.DelimitIdentifier(ColumnName(nameof(User.IsDeleted)));
        string preference = sql.DelimitIdentifier(ColumnName(nameof(User.LastActiveTenantId)));
        string function = FunctionSql(context), signature = function + "(uuid)";
        var contract = new PostgresPrivilegeGuard.Contract(schema, roles,
            [new(FunctionName, "uuid", OwnerRole, [GlobalRole])],
            [new(user.GetTableName()!, OwnerRole, [],
                [new(ColumnName(nameof(User.Id)), ["SELECT"]),
                    new(ColumnName(nameof(User.IsDeleted)), ["SELECT"]),
                    new(ColumnName(nameof(User.LastActiveTenantId)), ["SELECT"])])],
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract)
            + PostgresPrivilegeGuard.BuildRoutineAclAssertSql([signature], OwnerRole, GlobalRole, requireComplete: true)
            + $"""
            IF pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},'{OwnerRole}','MEMBER')
                OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},'{MigratorRole}','MEMBER')
                OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},'{GlobalRole}','MEMBER')
                OR pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)},'{OwnerRole}','MEMBER')
                OR pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)},'{MigratorRole}','MEMBER')
                OR EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles
                            WHERE rolname = {Text(enrollment.MigratorLogin)})
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN inherited_roles inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}'))
                OR pg_catalog.has_schema_privilege({Text(enrollment.RuntimeLogin)},{Text(schema)},'CREATE')
                OR pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)},{Text(schema)},'CREATE')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ({Text(enrollment.RuntimeLogin)},{Text(enrollment.GlobalLogin)})
                        AND (NOT rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication))
                OR pg_catalog.has_table_privilege({Text(enrollment.RuntimeLogin)},{Text(table)},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                OR pg_catalog.has_any_column_privilege({Text(enrollment.RuntimeLogin)},{Text(table)},'SELECT,INSERT,UPDATE,REFERENCES')
                OR pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)},{Text(table)},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},{Text(table)},'SELECT,INSERT,UPDATE,REFERENCES')
                OR pg_catalog.has_function_privilege({Text(enrollment.RuntimeLogin)},{Text(signature)},'EXECUTE') THEN
                RAISE EXCEPTION 'native_user_navigation_effective_privilege_drift' USING ERRCODE = '55000';
            END IF;
            """;
        string body = $"""
            BEGIN
                {guard}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF session_user <> {Text(enrollment.GlobalLogin)} OR current_user <> '{OwnerRole}' THEN
                    RAISE EXCEPTION 'native_user_navigation_wrong_executor' USING ERRCODE = '42501';
                END IF;
                RETURN QUERY SELECT u.{id}, u.{preference} FROM {table} u
                    WHERE u.{id} = p_user AND NOT u.{deleted};
            END;
            """;
        return new(schema, sql.DelimitIdentifier(schema), table, id, deleted, preference, function, signature, guard, body);
    }

    private sealed record Contract(string SchemaName, string Schema, string Table, string Id, string Deleted,
        string Preference, string Function, string Signature, string Guard, string Body);

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(User))!.GetSchema() ?? "public";

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
