using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Migration-owned queue enumeration exposes ownership only. Its executor cannot read
/// subscriber data or mutate claims; those operations require exact Runtime tenant authority.
/// </summary>
public static class PostgresIntegrationSyncDiscoveryContract
{
    public const string OwnerRole = "event_integration_sync_discovery_owner";
    public const string MigratorRole = "event_integration_sync_discovery_migrator";
    public const string RuntimeRole = "event_integration_sync_discovery_runtime";
    public const string PolicyName = "integration_sync_queue_discovery";

    public static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(IntegrationSyncOutbox))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_pending_owners", Schema(context, entity));
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
        var entity = context.Model.FindEntityType(typeof(IntegrationSyncOutbox))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context, entity));
        string table = sql.DelimitIdentifier(mapping.Name, Schema(context, entity));
        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(mapping)!);
        string tenant = Column(nameof(IntegrationSyncOutbox.TenantId));
        string id = Column(nameof(IntegrationSyncOutbox.Id));
        string status = Column(nameof(IntegrationSyncOutbox.Status));
        string next = Column(nameof(IntegrationSyncOutbox.NextAttemptAt));
        string processing = Column(nameof(IntegrationSyncOutbox.ProcessingStartedAt));
        string created = Column(nameof(IntegrationSyncOutbox.CreatedAt));
        string deleted = Column(nameof(IntegrationSyncOutbox.IsDeleted));
        string function = FunctionSql(context);
        string signature = $"{function}(timestamp with time zone, timestamp with time zone, integer)";
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [signature], OwnerRole, RuntimeRole, requireComplete: false);
        string currentAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [signature], OwnerRole, RuntimeRole, requireComplete: true);
        return $"""
            DO $prior_acl$ BEGIN {priorAcl} END; $prior_acl$;
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
                    RAISE EXCEPTION 'integration sync discovery roles must remain separate';
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
                    RAISE EXCEPTION 'integration sync discovery runtime and migration logins must remain separate';
                END IF;
            END
            $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {RuntimeRole};
            REVOKE ALL ON TABLE {table} FROM {OwnerRole};
            GRANT SELECT ({tenant}, {id}, {status}, {next}, {processing}, {created}, {deleted}) ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {table};
            CREATE POLICY "{PolicyName}" ON {table}
                FOR SELECT TO {OwnerRole} USING (true);
            DO $bootstrap$
            DECLARE
                v_previous_role text := current_setting('role');
            BEGIN
            PERFORM set_config('role', '{OwnerRole}', true);
            EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(
                p_now_utc timestamp with time zone, p_stale_before timestamp with time zone, p_take integer)
            RETURNS TABLE (tenant_id uuid, outbox_id uuid)
            LANGUAGE plpgsql STABLE SECURITY DEFINER
            SET search_path = pg_catalog
            AS $function$
            DECLARE
                v_now timestamp with time zone := LEAST(p_now_utc, statement_timestamp());
            BEGIN
                {currentAcl}
                IF p_now_utc IS NULL OR NOT isfinite(p_now_utc)
                   OR p_stale_before IS NULL OR NOT isfinite(p_stale_before)
                   OR p_stale_before > p_now_utc
                   OR p_take IS NULL OR p_take NOT BETWEEN 1 AND {IIntegrationSyncQueueDiscovery.MaximumBatchSize} THEN
                    RAISE EXCEPTION 'invalid integration sync discovery bounds' USING ERRCODE = '22023';
                END IF;
                RETURN QUERY
                    SELECT queue.{tenant}, queue.{id}
                    FROM {table} AS queue
                    WHERE NOT queue.{deleted}
                      AND (((queue.{status} = {(int)IntegrationSyncStatus.Pending}
                             OR queue.{status} = {(int)IntegrationSyncStatus.RetryScheduled})
                            AND (queue.{next} IS NULL OR queue.{next} <= v_now))
                           OR (queue.{status} = {(int)IntegrationSyncStatus.Processing}
                               AND (queue.{processing} IS NULL
                                    OR queue.{processing} <= LEAST(p_stale_before, v_now))))
                    ORDER BY queue.{created}, queue.{id}
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
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
