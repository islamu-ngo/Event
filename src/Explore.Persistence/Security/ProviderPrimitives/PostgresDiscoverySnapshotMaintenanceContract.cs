using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Grants only bounded expired ownership enumeration across tenant RLS. The function
/// owner can read header ownership and expiry, never membership or public source data.
/// Purging remains an ordinary exact-tenant transaction under FORCE RLS.
/// </summary>
public static class PostgresDiscoverySnapshotMaintenanceContract
{
    public const string OwnerRole = "event_discovery_maintenance_owner";
    public const string MigratorRole = "event_discovery_maintenance_migrator";
    public const string RuntimeRole = "event_discovery_maintenance_runtime";
    public const string PolicyName = "discovery_expired_ownership";

    public static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(EventDiscoverySnapshot))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_expired_owners", Schema(context, entity));
    }

    /// <summary>Called only with the existing application migration authority, never the API connection.</summary>
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
        var entity = context.Model.FindEntityType(typeof(EventDiscoverySnapshot))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context, entity));
        string table = sql.DelimitIdentifier(mapping.Name, Schema(context, entity));
        string tenant = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoverySnapshot.TenantId))!
            .GetColumnName(mapping)!);
        string expiry = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoverySnapshot.ExpiresAtUtc))!
            .GetColumnName(mapping)!);
        string function = FunctionSql(context);
        string signature = $"{function}(uuid, timestamp with time zone, integer)";
        return $"""
            DO $contract$
            DECLARE
                v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{RuntimeRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    ELSE
                        EXECUTE format('ALTER ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                END LOOP;
                IF pg_has_role('{RuntimeRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{RuntimeRole}', '{MigratorRole}', 'MEMBER')
                   OR pg_has_role('{OwnerRole}', '{RuntimeRole}', 'MEMBER')
                   OR pg_has_role('{MigratorRole}', '{RuntimeRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'discovery maintenance runtime and owner roles must remain separate';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        INNER JOIN inherited_roles AS inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{RuntimeRole}')) THEN
                    RAISE EXCEPTION 'discovery maintenance runtime and migration logins must remain separate';
                END IF;
            END
            $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {RuntimeRole};
            REVOKE ALL ON TABLE {table} FROM {OwnerRole};
            GRANT SELECT ({tenant}, {expiry}) ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {table};
            CREATE POLICY "{PolicyName}" ON {table}
                FOR SELECT TO {OwnerRole} USING (true);

            DO $bootstrap$
            DECLARE
                v_previous_role text := current_setting('role');
            BEGIN
            PERFORM set_config('role', '{OwnerRole}', true);
            EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(
                p_after_tenant uuid, p_now_utc timestamp with time zone, p_take integer)
            RETURNS TABLE ({tenant} uuid)
            LANGUAGE plpgsql
            STABLE
            SECURITY DEFINER
            SET search_path = pg_catalog
            AS $function$
            BEGIN
                IF p_now_utc IS NULL OR NOT isfinite(p_now_utc)
                   OR p_take IS NULL OR p_take NOT BETWEEN 1 AND {PurgeEventDiscoverySnapshotsCommandHandler.TenantBatchSize} THEN
                    RAISE EXCEPTION 'invalid discovery maintenance bounds' USING ERRCODE = '22023';
                END IF;
                RETURN QUERY
                    SELECT DISTINCT header.{tenant}
                    FROM {table} AS header
                    WHERE header.{expiry} <= LEAST(p_now_utc, statement_timestamp())
                      AND (p_after_tenant IS NULL OR header.{tenant} > p_after_tenant)
                    ORDER BY header.{tenant}
                    LIMIT p_take;
            END;
            $function$ $ddl$;
            EXECUTE $acl$
                REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {RuntimeRole};
                GRANT EXECUTE ON FUNCTION {signature} TO {RuntimeRole};
            $acl$;
            PERFORM set_config('role', v_previous_role, true);
            END
            $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            """;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
