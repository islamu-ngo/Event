using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Migration-owned incoming claim authority. Global can execute four bounded
/// functions but cannot select private rows or settle business effects.
/// Claim transitions and incoming attempt evidence commit in the same statement.
/// </summary>
public static partial class PostgresIncomingWebhookQueueContract
{
    public const string OwnerRole = "event_incoming_webhook_owner";
    public const string MigratorRole = "event_incoming_webhook_migrator";
    public const string GlobalRole = "event_incoming_webhook_global";
    public const string PolicyName = "incoming_webhook_queue_authority";

    public static string ClaimFunctionSql(ExploreDbContext context, bool effects) =>
        FunctionSql(context, effects ? "incoming_webhook_effect_claim" : "incoming_webhook_message_claim");

    public static string RenewFunctionSql(ExploreDbContext context, bool effects) =>
        FunctionSql(context, effects ? "incoming_webhook_effect_renew" : "incoming_webhook_message_renew");

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        enrollment.Validate();
        var contract = PrivilegeContract(context, enrollment);
        string assertion = PostgresPrivilegeGuard.BuildAssertSql(contract) + PostgresPrivilegeGuard.RestrictedCallerSql;
        string invocation = $"""
            {assertion}
            IF session_user IS DISTINCT FROM {Text(enrollment.GlobalLogin)} THEN
                RAISE EXCEPTION 'incoming_webhook_session_not_enrolled' USING ERRCODE = '42501';
            END IF;
            """;
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table(Type type)
        {
            var entity = context.Model.FindEntityType(type)!;
            return sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
        }
        string Column(Type type, string property)
        {
            var entity = context.Model.FindEntityType(type)!;
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string A(string property) => Column(typeof(IncomingWebhookProcessingAttempt), property);
        string attemptsTable = Table(typeof(IncomingWebhookProcessingAttempt));
        string[] attemptColumns =
        [
            nameof(IncomingWebhookProcessingAttempt.Id), nameof(IncomingWebhookProcessingAttempt.TenantId),
            nameof(IncomingWebhookProcessingAttempt.IncomingWebhookMessageId),
            nameof(IncomingWebhookProcessingAttempt.ProcessingGeneration),
            nameof(IncomingWebhookProcessingAttempt.ProcessingFence), nameof(IncomingWebhookProcessingAttempt.AttemptNumber),
            nameof(IncomingWebhookProcessingAttempt.OutcomeId), nameof(IncomingWebhookProcessingAttempt.StartedAt),
            nameof(IncomingWebhookProcessingAttempt.RecordedAt), nameof(IncomingWebhookProcessingAttempt.FailureCategory),
            nameof(IncomingWebhookProcessingAttempt.SafeDetail), nameof(IncomingWebhookProcessingAttempt.CreatedAt)
        ];
        string attemptList = string.Join(", ", attemptColumns.Select(A));
        string schema = sql.DelimitIdentifier(Schema(context));
        string messageSql = QueueSql(effects: false);
        string effectSql = QueueSql(effects: true);
        return $"""
            {EnrollmentSql(contract, enrollment)}
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {attemptsTable} FROM {OwnerRole}, {GlobalRole};
            GRANT INSERT ({attemptList}) ON {attemptsTable} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {attemptsTable};
            CREATE POLICY "{PolicyName}" ON {attemptsTable} FOR INSERT TO {OwnerRole} WITH CHECK (true);
            {messageSql}
            {effectSql}
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $ready$ BEGIN {PostgresPrivilegeGuard.BuildAssertSql(contract)} END $ready$;
            """;

        string QueueSql(bool effects)
        {
            Type type = effects ? typeof(IncomingWebhookEffectOutbox) : typeof(IncomingWebhookMessage);
            string C(string property) => Column(type, property);
            string table = Table(type);
            string id = C(nameof(IncomingWebhookMessage.Id));
            string tenant = C(nameof(IncomingWebhookMessage.TenantId));
            string status = C(effects ? nameof(IncomingWebhookEffectOutbox.Status) : nameof(IncomingWebhookMessage.StatusId));
            string next = C(nameof(IncomingWebhookMessage.NextAttemptAt));
            string expires = C(nameof(IncomingWebhookMessage.ProcessingLeaseExpiresAt));
            string token = C(nameof(IncomingWebhookMessage.ProcessingLeaseToken));
            string owner = C(nameof(IncomingWebhookMessage.ProcessingLeaseOwner));
            string fence = C(nameof(IncomingWebhookMessage.ProcessingFence));
            string generation = C(nameof(IncomingWebhookMessage.ProcessingGeneration));
            string attempt = C(nameof(IncomingWebhookMessage.AttemptCount));
            string started = C(nameof(IncomingWebhookMessage.ProcessingStartedAt));
            string created = C(effects ? nameof(IncomingWebhookEffectOutbox.CreatedAt) : nameof(IncomingWebhookMessage.ReceivedAt));
            string updated = C(nameof(IncomingWebhookMessage.UpdatedAt));
            string category = C(nameof(IncomingWebhookMessage.FailureCategory));
            string detail = C(nameof(IncomingWebhookMessage.SafeDetail));
            int pending = effects ? (int)OutboxMessageStatus.Pending : (int)IncomingWebhookMessageStatus.Verified;
            int retry = effects ? (int)OutboxMessageStatus.Failed : (int)IncomingWebhookMessageStatus.RetryDue;
            int processing = effects ? (int)OutboxMessageStatus.Processing : (int)IncomingWebhookMessageStatus.Processing;
            string claim = ClaimFunctionSql(context, effects);
            string renew = RenewFunctionSql(context, effects);
            string claimSignature = $"{claim}(text, integer, timestamp with time zone, interval)";
            string renewSignature = $"{renew}(uuid, uuid, uuid, bigint, integer, timestamp with time zone, timestamp with time zone)";
            string outputId = effects ? nameof(IncomingWebhookEffectClaim.EffectOutboxId) : nameof(IncomingWebhookClaim.IncomingWebhookMessageId);
            string expiredCategory = effects ? "coop_effect_lease_expired" : "processing_lease_expired";
            string expiredDetail = effects
                ? "The previous effect lease expired before settlement."
                : "The previous processing lease expired before settlement.";
            string expiredEvidence = effects ? "" : $"""
                INSERT INTO {attemptsTable} ({attemptList})
                VALUES (uuidv7(), q.tenant_id, q.queue_id, q.generation, q.fence, q.attempt,
                    {(int)IncomingWebhookProcessingAttemptOutcome.LeaseExpired},
                    coalesce(q.started, p_observed), p_observed, '{expiredCategory}', '{expiredDetail}', p_observed);
                """;
            string claimedEvidence = effects ? "" : $"""
                INSERT INTO {attemptsTable} ({attemptList})
                VALUES (uuidv7(), q.tenant_id, q.queue_id, q.generation, q.fence + 1, q.attempt + 1,
                    {(int)IncomingWebhookProcessingAttemptOutcome.Claimed},
                    p_observed, p_observed, NULL, NULL, p_observed);
                """;
            return $"""
                REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
                GRANT SELECT ({id}, {tenant}, {status}, {next}, {expires}, {token}, {fence}, {generation},
                    {attempt}, {started}, {created}) ON {table} TO {OwnerRole};
                GRANT UPDATE ({status}, {next}, {expires}, {token}, {owner}, {fence}, {attempt}, {started},
                    {updated}, {category}, {detail}) ON {table} TO {OwnerRole};
                DROP POLICY IF EXISTS "{PolicyName}" ON {table};
                CREATE POLICY "{PolicyName}" ON {table} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
                DO $bootstrap$
                DECLARE v_previous_role text := current_setting('role');
                BEGIN
                    PERFORM set_config('role', '{OwnerRole}', true);
                    EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {claim}(
                        p_owner text, p_take integer, p_observed timestamp with time zone, p_duration interval)
                    RETURNS TABLE ("{outputId}" uuid, "TenantId" uuid, "LeaseToken" uuid,
                        "ProcessingFence" bigint, "ProcessingGeneration" integer)
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $function$
                    DECLARE q record; v_token uuid;
                    BEGIN
                        {invocation}
                        IF p_owner IS NULL OR length(btrim(p_owner)) NOT BETWEEN 1 AND {IncomingWebhookMessage.MaxLeaseOwnerLength}
                           OR p_take IS NULL OR p_take NOT BETWEEN 1 AND {IIncomingWebhookQueueRepository.MaximumBatchSize}
                           OR p_observed IS NULL OR NOT isfinite(p_observed)
                           OR p_duration IS NULL OR NOT isfinite(p_duration) OR p_duration <= interval '0' THEN
                            RAISE EXCEPTION 'invalid incoming claim bounds' USING ERRCODE = '22023';
                        END IF;
                        FOR q IN
                            SELECT queue.{id} AS queue_id, queue.{tenant} AS tenant_id,
                                queue.{status} AS status, queue.{generation} AS generation,
                                queue.{fence} AS fence, queue.{attempt} AS attempt, queue.{started} AS started
                            FROM {table} AS queue
                            WHERE (queue.{status} IN ({pending}, {retry})
                                   AND (queue.{next} IS NULL OR queue.{next} <= p_observed))
                               OR (queue.{status} = {processing} AND queue.{expires} IS NOT NULL
                                   AND queue.{expires} <= p_observed)
                            ORDER BY coalesce(queue.{next}, queue.{created}), queue.{created}, queue.{id}
                            LIMIT p_take FOR UPDATE SKIP LOCKED
                        LOOP
                            IF q.status = {processing} THEN
                                {expiredEvidence}
                                UPDATE {table} SET {category} = '{expiredCategory}', {detail} = '{expiredDetail}'
                                    WHERE {tenant} = q.tenant_id AND {id} = q.queue_id;
                            END IF;
                            v_token := uuidv7();
                            UPDATE {table} SET {status} = {processing}, {next} = NULL,
                                {owner} = btrim(p_owner), {token} = v_token,
                                {expires} = p_observed + p_duration, {started} = p_observed,
                                {fence} = q.fence + 1, {attempt} = q.attempt + 1, {updated} = p_observed
                                WHERE {tenant} = q.tenant_id AND {id} = q.queue_id;
                            {claimedEvidence}
                            "{outputId}" := q.queue_id;
                            "TenantId" := q.tenant_id;
                            "LeaseToken" := v_token;
                            "ProcessingFence" := q.fence + 1;
                            "ProcessingGeneration" := q.generation;
                            RETURN NEXT;
                        END LOOP;
                    END $function$ $ddl$;
                    EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {renew}(
                        p_tenant uuid, p_id uuid, p_token uuid, p_fence bigint, p_generation integer,
                        p_observed timestamp with time zone, p_expires timestamp with time zone)
                    RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $function$
                    DECLARE v_updated integer;
                    BEGIN
                        {invocation}
                        IF p_observed IS NULL OR NOT isfinite(p_observed)
                           OR p_expires IS NULL OR NOT isfinite(p_expires) OR p_expires <= p_observed THEN
                            RETURN false;
                        END IF;
                        UPDATE {table} AS queue SET {expires} = p_expires, {updated} = p_observed
                            WHERE queue.{tenant} = p_tenant AND queue.{id} = p_id
                              AND queue.{status} = {processing} AND queue.{token} = p_token
                              AND queue.{fence} = p_fence AND queue.{generation} = p_generation
                              AND queue.{expires} > p_observed AND queue.{expires} < p_expires;
                        GET DIAGNOSTICS v_updated = ROW_COUNT;
                        RETURN v_updated = 1;
                    END $function$ $ddl$;
                    EXECUTE $acl$
                        REVOKE ALL ON FUNCTION {claimSignature}, {renewSignature} FROM PUBLIC, {GlobalRole};
                        GRANT EXECUTE ON FUNCTION {claimSignature}, {renewSignature} TO {GlobalRole};
                    $acl$;
                    PERFORM set_config('role', v_previous_role, true);
                END $bootstrap$;
                """;
        }
    }

    private static string FunctionSql(ExploreDbContext context, string name) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, Schema(context));

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(IncomingWebhookMessage))!.GetSchema() ?? "public";
}
