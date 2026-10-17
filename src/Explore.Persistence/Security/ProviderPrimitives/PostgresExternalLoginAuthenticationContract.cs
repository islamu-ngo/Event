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

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
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
        return $"""
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
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
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
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                    SELECT binding.* FROM {table} AS binding
                    WHERE binding.{provider} = p_provider_id AND binding.{key} = p_provider_key;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {function}(integer, text) FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {function}(integer, text) TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            """;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
