using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Migration-owned, execute-only health discovery returns thirteen fixed counts.
/// The function owner can read only predicate columns, never queue payloads or private identities.
/// </summary>
public static class PostgresQueueDrainHealthContract
{
    public const string OwnerRole = "event_queue_drain_health_owner";
    public const string MigratorRole = "event_queue_drain_health_migrator";
    public const string GlobalRole = "event_queue_drain_health_global";
    public const string PolicyName = "queue_drain_health_read";

    public static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(IntegrationSyncOutbox))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "queue_drain_health_snapshot", Schema(context, entity));
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
        var sql = context.GetService<ISqlGenerationHelper>();
        IEntityType Entity(Type type) => context.Model.FindEntityType(type)!;
        string Table(Type type)
        {
            var entity = Entity(type);
            return sql.DelimitIdentifier(entity.GetTableName()!, Schema(context, entity));
        }
        string Column(Type type, string property)
        {
            var entity = Entity(type);
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string C<T>(string property) => Column(typeof(T), property);
        (Type Type, string[] Columns)[] grants =
        [
            (typeof(IntegrationSyncOutbox), [nameof(IntegrationSyncOutbox.Status),
                nameof(IntegrationSyncOutbox.NextAttemptAt), nameof(IntegrationSyncOutbox.ProcessingStartedAt),
                nameof(IntegrationSyncOutbox.LastError), nameof(IntegrationSyncOutbox.IsDeleted)]),
            (typeof(IncomingWebhookMessage), [nameof(IncomingWebhookMessage.StatusId),
                nameof(IncomingWebhookMessage.NextAttemptAt), nameof(IncomingWebhookMessage.ProcessingLeaseExpiresAt)]),
            (typeof(WebhookBulkReplayOperation), [nameof(WebhookBulkReplayOperation.StatusId)]),
            (typeof(WebhookProviderPublication), [nameof(WebhookProviderPublication.StatusId),
                nameof(WebhookProviderPublication.NextActionAt), nameof(WebhookProviderPublication.ProcessingLeaseExpiresAt)]),
            (typeof(PdsSyncOutbox), [nameof(PdsSyncOutbox.Status), nameof(PdsSyncOutbox.NextRetryAt),
                nameof(PdsSyncOutbox.LeaseExpiresAt), nameof(PdsSyncOutbox.SupersededById)])
        ];
        string tableGrants = string.Join("\n", grants.Select(grant => $"""
            REVOKE ALL ON TABLE {Table(grant.Type)} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({string.Join(", ", grant.Columns.Select(column => Column(grant.Type, column)))})
                ON {Table(grant.Type)} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {Table(grant.Type)};
            CREATE POLICY "{PolicyName}" ON {Table(grant.Type)} FOR SELECT TO {OwnerRole} USING (true);
            """));
        string schema = sql.DelimitIdentifier(Schema(context, Entity(typeof(IntegrationSyncOutbox))));
        string function = FunctionSql(context);
        string signature = $"{function}(timestamp with time zone, timestamp with time zone)";
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [signature], OwnerRole, GlobalRole, requireComplete: false);
        string currentAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [signature], OwnerRole, GlobalRole, requireComplete: true);
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
                    RAISE EXCEPTION 'queue health execution and migration authority must remain separate';
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
                    RAISE EXCEPTION 'queue health execution and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {tableGrants}
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(
                    p_observed_at timestamp with time zone, p_integration_stale_before timestamp with time zone)
                RETURNS TABLE (
                    "IntegrationDue" integer, "IntegrationStale" integer, "IntegrationAmbiguous" integer,
                    "IncomingDue" integer, "IncomingStale" integer,
                    "BulkReplayQueued" integer, "BulkReplayExecuting" integer,
                    "ProviderPublicationDue" integer, "ProviderPublicationStale" integer,
                    "ProviderPublicationUnknown" integer,
                    "PdsDue" integer, "PdsStale" integer, "PdsDeadLettered" integer)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {currentAcl}
                    IF p_observed_at IS NULL OR NOT isfinite(p_observed_at)
                       OR p_integration_stale_before IS NULL OR NOT isfinite(p_integration_stale_before) THEN
                        RAISE EXCEPTION 'invalid queue health observation timestamps' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY
                    SELECT integration.due, integration.stale, integration.ambiguous,
                        incoming.due, incoming.stale, replay.queued, replay.executing,
                        publication.due, publication.stale, publication.unknown,
                        pds.due, pds.stale, pds.dead_lettered
                    FROM (
                        SELECT
                            (count(*) FILTER (WHERE q.{C<IntegrationSyncOutbox>(nameof(IntegrationSyncOutbox.Status))}
                                IN ({(int)IntegrationSyncStatus.Pending}, {(int)IntegrationSyncStatus.RetryScheduled})
                                AND (q.{C<IntegrationSyncOutbox>(nameof(IntegrationSyncOutbox.NextAttemptAt))} IS NULL
                                     OR q.{C<IntegrationSyncOutbox>(nameof(IntegrationSyncOutbox.NextAttemptAt))} <= p_observed_at)))::integer AS due,
                            (count(*) FILTER (WHERE q.{C<IntegrationSyncOutbox>(nameof(IntegrationSyncOutbox.Status))} = {(int)IntegrationSyncStatus.Processing}
                                AND (q.{C<IntegrationSyncOutbox>(nameof(IntegrationSyncOutbox.ProcessingStartedAt))} IS NULL
                                     OR q.{C<IntegrationSyncOutbox>(nameof(IntegrationSyncOutbox.ProcessingStartedAt))} <= p_integration_stale_before)))::integer AS stale,
                            (count(*) FILTER (WHERE q.{C<IntegrationSyncOutbox>(nameof(IntegrationSyncOutbox.Status))} = {(int)IntegrationSyncStatus.DeadLettered}
                                AND q.{C<IntegrationSyncOutbox>(nameof(IntegrationSyncOutbox.LastError))} = '{IntegrationSyncFailureCodes.ProviderOutcomeAmbiguous}'))::integer AS ambiguous
                        FROM {Table(typeof(IntegrationSyncOutbox))} AS q
                        WHERE NOT q.{C<IntegrationSyncOutbox>(nameof(IntegrationSyncOutbox.IsDeleted))}
                    ) AS integration
                    CROSS JOIN (
                        SELECT
                            (count(*) FILTER (WHERE q.{C<IncomingWebhookMessage>(nameof(IncomingWebhookMessage.StatusId))}
                                IN ({(int)IncomingWebhookMessageStatus.Verified}, {(int)IncomingWebhookMessageStatus.RetryDue})
                                AND (q.{C<IncomingWebhookMessage>(nameof(IncomingWebhookMessage.NextAttemptAt))} IS NULL
                                     OR q.{C<IncomingWebhookMessage>(nameof(IncomingWebhookMessage.NextAttemptAt))} <= p_observed_at)))::integer AS due,
                            (count(*) FILTER (WHERE q.{C<IncomingWebhookMessage>(nameof(IncomingWebhookMessage.StatusId))} = {(int)IncomingWebhookMessageStatus.Processing}
                                AND (q.{C<IncomingWebhookMessage>(nameof(IncomingWebhookMessage.ProcessingLeaseExpiresAt))} IS NULL
                                     OR q.{C<IncomingWebhookMessage>(nameof(IncomingWebhookMessage.ProcessingLeaseExpiresAt))} <= p_observed_at)))::integer AS stale
                        FROM {Table(typeof(IncomingWebhookMessage))} AS q
                    ) AS incoming
                    CROSS JOIN (
                        SELECT
                            (count(*) FILTER (WHERE q.{C<WebhookBulkReplayOperation>(nameof(WebhookBulkReplayOperation.StatusId))} = {(int)WebhookBulkReplayStatus.Queued}))::integer AS queued,
                            (count(*) FILTER (WHERE q.{C<WebhookBulkReplayOperation>(nameof(WebhookBulkReplayOperation.StatusId))} = {(int)WebhookBulkReplayStatus.Executing}))::integer AS executing
                        FROM {Table(typeof(WebhookBulkReplayOperation))} AS q
                    ) AS replay
                    CROSS JOIN (
                        SELECT
                            (count(*) FILTER (WHERE q.{C<WebhookProviderPublication>(nameof(WebhookProviderPublication.StatusId))}
                                IN ({(int)WebhookProviderPublicationStatus.Prepared}, {(int)WebhookProviderPublicationStatus.RetryDue})
                                AND (q.{C<WebhookProviderPublication>(nameof(WebhookProviderPublication.NextActionAt))} IS NULL
                                     OR q.{C<WebhookProviderPublication>(nameof(WebhookProviderPublication.NextActionAt))} <= p_observed_at)))::integer AS due,
                            (count(*) FILTER (WHERE q.{C<WebhookProviderPublication>(nameof(WebhookProviderPublication.StatusId))} = {(int)WebhookProviderPublicationStatus.Publishing}
                                AND (q.{C<WebhookProviderPublication>(nameof(WebhookProviderPublication.ProcessingLeaseExpiresAt))} IS NULL
                                     OR q.{C<WebhookProviderPublication>(nameof(WebhookProviderPublication.ProcessingLeaseExpiresAt))} <= p_observed_at)))::integer AS stale,
                            (count(*) FILTER (WHERE q.{C<WebhookProviderPublication>(nameof(WebhookProviderPublication.StatusId))} = {(int)WebhookProviderPublicationStatus.PublicationUnknown}))::integer AS unknown
                        FROM {Table(typeof(WebhookProviderPublication))} AS q
                    ) AS publication
                    CROSS JOIN (
                        SELECT
                            (count(*) FILTER (WHERE q.{C<PdsSyncOutbox>(nameof(PdsSyncOutbox.SupersededById))} IS NULL
                                AND q.{C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))}
                                    IN ({(int)PdsSyncStatus.Pending}, {(int)PdsSyncStatus.Failed})
                                AND (q.{C<PdsSyncOutbox>(nameof(PdsSyncOutbox.NextRetryAt))} IS NULL
                                     OR q.{C<PdsSyncOutbox>(nameof(PdsSyncOutbox.NextRetryAt))} <= p_observed_at)))::integer AS due,
                            (count(*) FILTER (WHERE q.{C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))} = {(int)PdsSyncStatus.Processing}
                                AND (q.{C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseExpiresAt))} IS NULL
                                     OR q.{C<PdsSyncOutbox>(nameof(PdsSyncOutbox.LeaseExpiresAt))} <= p_observed_at)))::integer AS stale,
                            (count(*) FILTER (WHERE q.{C<PdsSyncOutbox>(nameof(PdsSyncOutbox.Status))} = {(int)PdsSyncStatus.DeadLettered}))::integer AS dead_lettered
                        FROM {Table(typeof(PdsSyncOutbox))} AS q
                    ) AS pds;
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
