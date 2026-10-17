using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Grants exact credential lookup and two-column usage touch, never direct private-table access to its executor.</summary>
public static class PostgresExternalApiKeyAuthenticationContract
{
    public const string OwnerRole = "event_api_key_authentication_owner";
    public const string MigratorRole = "event_api_key_authentication_migrator";
    public const string GlobalRole = "event_api_key_authentication_global";

    public static string FunctionSql(ExploreDbContext context) => Function(context, "_for_authentication");
    public static string UsageFunctionSql(ExploreDbContext context) => Function(context, "_touch_usage");

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
        var entity = context.Model.FindEntityType(typeof(ExternalApiKey))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context, entity));
        string table = sql.DelimitIdentifier(mapping.Name, Schema(context, entity));
        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(mapping)!);
        string columns = string.Join(", ", entity.GetProperties()
            .Select(property => sql.DelimitIdentifier(property.GetColumnName(mapping)!)));
        string id = Column(nameof(ExternalApiKey.Id));
        string keyId = Column(nameof(ExternalApiKey.KeyId));
        string lastUsedAt = Column(nameof(ExternalApiKey.LastUsedAt));
        string lastUsedIp = Column(nameof(ExternalApiKey.LastUsedIp));
        string lookup = FunctionSql(context);
        string touch = UsageFunctionSql(context);
        string[] signatures = [$"{lookup}(text)", $"{touch}(uuid, timestamp with time zone, text, timestamp with time zone)"];
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            signatures, OwnerRole, GlobalRole, requireComplete: false);
        string currentAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            signatures, OwnerRole, GlobalRole, requireComplete: true);
        return $"""
            DO $prior_acl$ BEGIN {priorAcl} END; $prior_acl$;
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
                    RAISE EXCEPTION 'API key authentication execution and migration authority must remain separate';
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
                    RAISE EXCEPTION 'API key authentication execution and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({columns}) ON {table} TO {OwnerRole};
            GRANT UPDATE ({lastUsedAt}, {lastUsedIp}) ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS api_key_authentication_read ON {table};
            CREATE POLICY api_key_authentication_read ON {table} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS api_key_authentication_usage ON {table};
            CREATE POLICY api_key_authentication_usage ON {table} FOR UPDATE TO {OwnerRole}
                USING (true) WITH CHECK (true);
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {lookup}(p_key_id text)
                RETURNS SETOF {table}
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {currentAcl}
                    RETURN QUERY SELECT credential.* FROM {table} AS credential WHERE credential.{keyId} = p_key_id;
                END;
                $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {touch}(
                    p_id uuid, p_used_at timestamp with time zone, p_last_ip text,
                    p_threshold timestamp with time zone)
                RETURNS boolean
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE v_touched boolean;
                BEGIN
                    {currentAcl}
                    WITH touched AS (
                        UPDATE {table} SET {lastUsedAt} = p_used_at, {lastUsedIp} = p_last_ip
                        WHERE {id} = p_id AND ({lastUsedAt} IS NULL OR {lastUsedAt} < p_threshold)
                        RETURNING 1)
                    SELECT EXISTS (SELECT 1 FROM touched) INTO v_touched;
                    RETURN v_touched;
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {lookup}(text) FROM PUBLIC, {GlobalRole};
                    REVOKE ALL ON FUNCTION {touch}(uuid, timestamp with time zone, text, timestamp with time zone) FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {lookup}(text) TO {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {touch}(uuid, timestamp with time zone, text, timestamp with time zone) TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }

    private static string Function(ExploreDbContext context, string suffix)
    {
        var entity = context.Model.FindEntityType(typeof(ExternalApiKey))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + suffix, Schema(context, entity));
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
