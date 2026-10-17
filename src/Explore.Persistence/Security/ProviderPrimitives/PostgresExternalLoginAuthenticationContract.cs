using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Exposes exact provider-account binding reads without user PII joins or identity-write authority.</summary>
public static class PostgresExternalLoginAuthenticationContract
{
    public const string OwnerRole = "event_external_login_authentication_owner";
    public const string MigratorRole = "event_external_login_authentication_migrator";
    public const string GlobalRole = "event_external_login_authentication_global";

    public static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(UserExternalLogin))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_for_authentication", Schema(context, entity));
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var entity = context.Model.FindEntityType(typeof(UserExternalLogin))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context, entity));
        string table = sql.DelimitIdentifier(mapping.Name, Schema(context, entity));
        string columns = string.Join(", ", entity.GetProperties()
            .Select(property => sql.DelimitIdentifier(property.GetColumnName(mapping)!)));
        string provider = sql.DelimitIdentifier(entity.FindProperty(nameof(UserExternalLogin.AuthenticationProviderId))!
            .GetColumnName(mapping)!);
        string key = sql.DelimitIdentifier(entity.FindProperty(nameof(UserExternalLogin.ProviderKey))!
            .GetColumnName(mapping)!);
        string function = FunctionSql(context);
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [$"{function}(integer, text)"], OwnerRole, GlobalRole, requireComplete: false);
        string guard = EnrollmentGuard(context, enrollment);
        return $"""
            DO $prior_acl$ BEGIN {priorAcl} END $prior_acl$;
            DO $prior_enrollment$ BEGIN
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')) THEN
                    {guard}
                END IF;
            END $prior_enrollment$;
            DO $contract$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    ELSE
                        EXECUTE format('ALTER ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                END LOOP;
                IF pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER')
                   OR pg_has_role('{OwnerRole}', '{GlobalRole}', 'MEMBER')
                   OR pg_has_role('{MigratorRole}', '{GlobalRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'external-login authentication execution and migration authority must remain separate';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        INNER JOIN inherited_roles AS inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'external-login authentication execution and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT {MigratorRole} TO {Quote(enrollment.MigratorLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT {GlobalRole} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({columns}) ON {table} TO {OwnerRole};
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(p_provider_id integer, p_provider_key text)
                RETURNS SETOF {table}
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    IF session_user <> '{Literal(enrollment.GlobalLogin)}' OR current_user <> '{OwnerRole}' THEN
                        RAISE EXCEPTION 'native_external_login_authentication_wrong_executor' USING ERRCODE = '42501';
                    END IF;
                    RETURN QUERY SELECT binding.* FROM {table} AS binding
                    WHERE binding.{provider} = p_provider_id AND binding.{key} = p_provider_key;
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {function}(integer, text) FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {function}(integer, text) TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_enrollment$ BEGIN {guard} END $current_enrollment$;
            """;
    }

    private static string EnrollmentGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        string[] roles = [OwnerRole, MigratorRole, GlobalRole];
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(roles.Contains))
            throw new InvalidOperationException("external_login_authentication_enrollment_collision");
        var entity = context.Model.FindEntityType(typeof(UserExternalLogin))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = Schema(context, entity);
        var columns = entity.GetProperties().Select(property =>
            new PostgresPrivilegeGuard.Column(property.GetColumnName(mapping)!, ["SELECT"])).ToArray();
        var contract = new PostgresPrivilegeGuard.Contract(schema, roles,
            [new(mapping.Name + "_for_authentication", "integer, text", OwnerRole, [GlobalRole])],
            [new(mapping.Name, OwnerRole, [], columns)],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(mapping.Name, schema);
        return PostgresPrivilegeGuard.BuildAssertSql(contract) + $"""
            IF pg_catalog.pg_has_role('{Literal(enrollment.RuntimeLogin)}','{OwnerRole}','MEMBER')
                OR pg_catalog.pg_has_role('{Literal(enrollment.RuntimeLogin)}','{MigratorRole}','MEMBER')
                OR pg_catalog.pg_has_role('{Literal(enrollment.RuntimeLogin)}','{GlobalRole}','MEMBER')
                OR pg_catalog.pg_has_role('{Literal(enrollment.GlobalLogin)}','{OwnerRole}','MEMBER')
                OR pg_catalog.pg_has_role('{Literal(enrollment.GlobalLogin)}','{MigratorRole}','MEMBER')
                OR EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles
                            WHERE rolname = '{Literal(enrollment.MigratorLogin)}')
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN inherited_roles inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles WHERE roleid =
                        (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{Literal(enrollment.RuntimeLogin)}','{Literal(enrollment.GlobalLogin)}')
                    AND (NOT rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication))
                OR pg_catalog.has_schema_privilege('{Literal(enrollment.RuntimeLogin)}','{Literal(schema)}','CREATE')
                OR pg_catalog.has_schema_privilege('{Literal(enrollment.GlobalLogin)}','{Literal(schema)}','CREATE')
                OR pg_catalog.has_table_privilege('{Literal(enrollment.GlobalLogin)}','{Literal(table)}','SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                OR pg_catalog.has_any_column_privilege('{Literal(enrollment.GlobalLogin)}','{Literal(table)}','SELECT,INSERT,UPDATE,REFERENCES')
                OR pg_catalog.has_function_privilege('{Literal(enrollment.RuntimeLogin)}',
                    '{Literal(FunctionSql(context))}(integer,text)','EXECUTE') THEN
                RAISE EXCEPTION 'native_external_login_authentication_enrollment_drift' USING ERRCODE = '55000';
            END IF;
            """;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Literal(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
