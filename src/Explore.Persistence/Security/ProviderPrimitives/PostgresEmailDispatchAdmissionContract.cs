using System.ComponentModel.DataAnnotations.Schema;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Global execution is restricted to bounded admission, stale recovery and health aggregates.
/// The non-login definer has ledger-column grants, not message-content access.
/// </summary>
public static class PostgresEmailDispatchAdmissionContract
{
    public const string OwnerRole = "event_email_admission_owner";
    public const string MigratorRole = "event_email_admission_migrator";
    public const string GlobalRole = "event_email_admission_global";
    private const string PolicyName = "email_dispatch_admission";

    public static string ClaimFunctionSql(ExploreDbContext context) => FunctionSql(context, "_admit");
    public static string RecoveryFunctionSql(ExploreDbContext context) => FunctionSql(context, "_recover");
    public static string HealthFunctionSql(ExploreDbContext context) => FunctionSql(context, "_health");

    private static string FunctionSql(ExploreDbContext context, string suffix)
    {
        var entity = context.Model.FindEntityType(typeof(EmailDispatchOutbox))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + suffix, Schema(context, entity));
    }

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal sealed record ClaimedOwner(Guid TenantId, Guid OutboxId);
    private sealed record RecoveryCounts(int RetryScheduledCount, int UnknownCount);

    private sealed record HealthRow(
        [property: Column("due_dispatch_count")] int DueDispatchCount,
        [property: Column("retry_scheduled_count")] int RetryScheduledCount,
        [property: Column("stale_processing_count")] int StaleProcessingCount,
        [property: Column("dead_lettered_count")] int DeadLetteredCount,
        [property: Column("unknown_count")] int UnknownCount,
        [property: Column("parked_count")] int ParkedCount,
        [property: Column("oldest_due_created_at")] DateTime? OldestDueCreatedAt,
        [property: Column("optional_reminder_deferral_active")] bool OptionalReminderDeferralActive,
        [property: Column("global_paused")] bool GlobalPaused,
        [property: Column("global_smtp_rate_limit_override_active")] bool GlobalSmtpRateLimitOverrideActive,
        [property: Column("tenant_id")] Guid? TenantId,
        [property: Column("tenant_due_count")] int? TenantDueCount);

    internal static async Task<EmailDispatchHealthSnapshot> ReadHealthAsync(
        ExploreDbContext context, DateTime now, DateTime processingStartedBefore,
        int tenantSampleLimit, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQueryRaw<HealthRow>(
                $"SELECT * FROM {HealthFunctionSql(context)}({{0}}, {{1}}, {{2}})",
                now, processingStartedBefore, tenantSampleLimit)
            .ToListAsync(cancellationToken);
        var totals = rows[0];
        return new EmailDispatchHealthSnapshot(
            totals.DueDispatchCount, totals.RetryScheduledCount, totals.StaleProcessingCount,
            totals.DeadLetteredCount, totals.UnknownCount, totals.ParkedCount, totals.OldestDueCreatedAt,
            totals.OptionalReminderDeferralActive, totals.GlobalPaused, totals.GlobalSmtpRateLimitOverrideActive,
            rows.Where(row => row.TenantId.HasValue)
                .ToDictionary(row => row.TenantId!.Value, row => row.TenantDueCount!.Value));
    }

    internal static async Task<IReadOnlyList<ClaimedOwner>> ClaimAsync(
        ExploreDbContext context, EmailDispatchBatchClaimRequest request,
        Guid? tenantId, Guid? publishEventId, CancellationToken cancellationToken)
    {
        string query = $"SELECT * FROM {ClaimFunctionSql(context)}" +
            "(@lease, @claimed, @take, @per_tenant, @global_limit, @tenant_limit, @high, @low, @tenant, @publish)";
        object[] parameters =
        [
            new NpgsqlParameter("lease", request.LeaseToken),
            new NpgsqlParameter("claimed", request.ClaimedAt),
            new NpgsqlParameter("take", request.BatchSize),
            new NpgsqlParameter("per_tenant", request.MaxRowsPerTenant),
            new NpgsqlParameter("global_limit", request.GlobalProcessingLimit),
            new NpgsqlParameter("tenant_limit", request.TenantProcessingLimit),
            new NpgsqlParameter("high", request.OptionalReminderBacklogHighWatermark),
            new NpgsqlParameter("low", request.OptionalReminderBacklogLowWatermark),
            new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenantId ?? DBNull.Value },
            new NpgsqlParameter("publish", NpgsqlDbType.Uuid) { Value = (object?)publishEventId ?? DBNull.Value }
        ];
        return await context.Database.SqlQueryRaw<ClaimedOwner>(query, parameters).ToListAsync(cancellationToken);
    }

    internal static async Task<EmailDispatchStaleRecoveryResult> RecoverAsync(
        ExploreDbContext context, EmailDispatchStaleRecoveryRequest request, CancellationToken cancellationToken)
    {
        string query = $"SELECT * FROM {RecoveryFunctionSql(context)}" +
            "({0}, {1}, {2}, {3}, {4}, {5}, {6})";
        var result = await context.Database.SqlQueryRaw<RecoveryCounts>(query,
                request.ProcessingStartedBefore, request.RecoveredAt, request.BatchSize,
                request.RetryFailureCategory, request.RetryErrorMessage,
                request.UnknownFailureCategory, request.UnknownErrorMessage)
            .SingleAsync(cancellationToken);
        return new EmailDispatchStaleRecoveryResult(result.RetryScheduledCount, result.UnknownCount);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var outboxEntity = context.Model.FindEntityType(typeof(EmailDispatchOutbox))!;
        string schema = sql.DelimitIdentifier(Schema(context, outboxEntity));
        string Table<T>()
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.GetTableName()!, Schema(context, entity));
        }
        string outbox = Table<EmailDispatchOutbox>();
        string attempts = Table<EmailDispatchAttempt>();
        string receipts = Table<EmailDispatchReceipt>();
        string deliveries = Table<NotificationDelivery>();
        string controls = Table<EmailDispatchTenantControl>();
        string states = Table<EmailDispatchProcessorState>();
        string claim = ClaimFunctionSql(context);
        string recovery = RecoveryFunctionSql(context);
        string health = HealthFunctionSql(context);
        string claimSignature = $"{claim}(uuid, timestamp with time zone, integer, integer, integer, integer, integer, integer, uuid, uuid)";
        string recoverySignature = $"{recovery}(timestamp with time zone, timestamp with time zone, integer, text, text, text, text)";
        string healthSignature = $"{health}(timestamp with time zone, timestamp with time zone, integer)";
        string expectedFunctions = string.Join(", ", new[] { claimSignature, recoverySignature, healthSignature }
            .Select(signature => $"('{signature.Replace("'", "''", StringComparison.Ordinal)}')"));
        long lockKey = RelationalNamedLock.ComputeStableKey(EmailDispatchOutboxRepository.ClaimAdvisoryLockName);
        int pending = (int)EmailDispatchStatus.Pending;
        int retry = (int)EmailDispatchStatus.RetryScheduled;
        int processing = (int)EmailDispatchStatus.Processing;
        int reminder = (int)EmailDispatchKind.EventReminder;
        int unknown = (int)EmailDispatchStatus.Unknown;
        return $"""
            DO $contract$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = v_role AND (rolcanlogin OR rolsuper OR rolcreatedb
                            OR rolcreaterole OR rolinherit OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'email_admission_role_invalid' USING ERRCODE = '42501';
                    END IF;
                END LOOP;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_auth_members edge
                    JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                    JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                    WHERE member.rolname IN ('{OwnerRole}', '{GlobalRole}')
                        OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                        OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}'
                        OR parent.rolname = '{MigratorRole}' AND member.rolname <> CURRENT_USER
                        OR parent.rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')
                            AND edge.admin_option
                        OR parent.rolname = '{OwnerRole}'
                            AND (edge.inherit_option OR NOT edge.set_option)
                        OR parent.rolname = '{MigratorRole}' AND NOT edge.set_option) THEN
                    RAISE EXCEPTION 'email_admission_membership_invalid' USING ERRCODE = '42501';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_shdepend dependency
                    JOIN pg_catalog.pg_roles role ON role.oid = dependency.refobjid
                    WHERE dependency.refclassid = 'pg_catalog.pg_authid'::regclass
                        AND role.rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')
                        AND dependency.deptype = 'o'
                        AND (role.rolname <> '{OwnerRole}'
                            OR dependency.classid <> 'pg_catalog.pg_proc'::regclass
                            OR dependency.dbid <> (SELECT oid FROM pg_catalog.pg_database
                                WHERE datname = pg_catalog.current_database())
                            OR NOT EXISTS (
                                SELECT 1 FROM (VALUES {expectedFunctions}) expected(signature)
                                WHERE dependency.objid = pg_catalog.to_regprocedure(expected.signature)))) THEN
                    RAISE EXCEPTION 'email_admission_ownership_invalid' USING ERRCODE = '42501';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_class relation
                    JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                    WHERE namespace.nspname <> 'information_schema' AND namespace.nspname !~ '^pg_'
                        AND (relation.relkind IN ('r', 'p', 'v', 'm', 'f')
                            AND (has_table_privilege('{GlobalRole}', relation.oid,
                                'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR has_any_column_privilege('{GlobalRole}', relation.oid,
                                    'SELECT,INSERT,UPDATE,REFERENCES'))
                            OR relation.relkind = 'S' AND has_sequence_privilege(
                                '{GlobalRole}', relation.oid, 'USAGE,SELECT,UPDATE'))) THEN
                    RAISE EXCEPTION 'email_admission_execution_group_table_privilege' USING ERRCODE = '42501';
                END IF;
                IF pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER')
                   OR pg_has_role('{OwnerRole}', '{GlobalRole}', 'MEMBER')
                   OR pg_has_role('{MigratorRole}', '{GlobalRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'email admission global and migration authority must remain separate';
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
                    RAISE EXCEPTION 'email admission global and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE;
            GRANT {MigratorRole} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {outbox}, {attempts}, {receipts}, {deliveries}, {controls}, {states}
                FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT (id, tenant_id, publish_event_id, status, processing_lease_token,
                processing_started_at, created_at, kind, content_redacted_at, is_deleted,
                next_attempt_at, attempt_count) ON {outbox} TO {OwnerRole};
            GRANT UPDATE (status, processing_lease_token, processing_started_at, updated_at,
                next_attempt_at, unknown_at, last_failure_category, last_error, last_failure_at)
                ON {outbox} TO {OwnerRole};
            GRANT SELECT (tenant_id, email_dispatch_outbox_id, attempt_number, failure_category)
                ON {attempts} TO {OwnerRole};
            GRANT UPDATE (outcome, completed_at, failure_category, sanitized_error_message,
                provider_message_id, updated_at) ON {attempts} TO {OwnerRole};
            GRANT SELECT (tenant_id, email_dispatch_outbox_id, status) ON {receipts} TO {OwnerRole};
            GRANT UPDATE (status, failed_at, failure_code, failure_message, updated_at) ON {receipts} TO {OwnerRole};
            GRANT SELECT (tenant_id, email_dispatch_outbox_id, is_required, channel_id) ON {deliveries} TO {OwnerRole};
            GRANT UPDATE (status_id, provider_message_id, provider_status, failure_category, completed_at, updated_at)
                ON {deliveries} TO {OwnerRole};
            GRANT SELECT (tenant_id, is_paused) ON {controls} TO {OwnerRole};
            GRANT SELECT (processor_code, is_paused, optional_reminders_deferred,
                global_smtp_rate_limit_per_minute_override) ON {states} TO {OwnerRole};
            GRANT INSERT (id, processor_code, is_paused, optional_reminders_deferred, updated_at) ON {states} TO {OwnerRole};
            GRANT UPDATE (optional_reminders_deferred, updated_at) ON {states} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {outbox};
            CREATE POLICY "{PolicyName}" ON {outbox} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {attempts};
            CREATE POLICY "{PolicyName}" ON {attempts} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {receipts};
            CREATE POLICY "{PolicyName}" ON {receipts} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {deliveries};
            CREATE POLICY "{PolicyName}" ON {deliveries} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {controls};
            CREATE POLICY "{PolicyName}" ON {controls} FOR SELECT TO {OwnerRole} USING (true);
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {health}(
                    p_now timestamp with time zone, p_before timestamp with time zone, p_tenant_limit integer)
                RETURNS TABLE (
                    due_dispatch_count integer, retry_scheduled_count integer, stale_processing_count integer,
                    dead_lettered_count integer, unknown_count integer, parked_count integer,
                    oldest_due_created_at timestamp with time zone, optional_reminder_deferral_active boolean,
                    global_paused boolean, global_smtp_rate_limit_override_active boolean,
                    tenant_id uuid, tenant_due_count integer)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    IF p_now IS NULL OR NOT isfinite(p_now)
                       OR p_before IS NULL OR NOT isfinite(p_before) OR p_before > p_now
                       OR p_tenant_limit IS NULL OR p_tenant_limit NOT BETWEEN 1 AND 100 THEN
                        RAISE EXCEPTION 'invalid email health bounds' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY
                    WITH active AS MATERIALIZED (
                        SELECT queue.tenant_id, queue.status, queue.created_at, queue.processing_started_at,
                            queue.status IN ({pending}, {retry})
                                AND (queue.next_attempt_at IS NULL OR queue.next_attempt_at <= p_now) AS due
                        FROM {outbox} AS queue
                        WHERE NOT queue.is_deleted AND queue.content_redacted_at IS NULL
                          AND NOT EXISTS (SELECT 1 FROM {controls} AS control
                              WHERE control.tenant_id = queue.tenant_id AND control.is_paused)
                    ), totals AS (
                        SELECT (COUNT(*) FILTER (WHERE active.due))::integer AS due_count,
                            (COUNT(*) FILTER (WHERE active.status = {retry}))::integer AS retry_count,
                            (COUNT(*) FILTER (WHERE active.status = {processing}
                                AND active.processing_started_at <= p_before))::integer AS stale_count,
                            (COUNT(*) FILTER (WHERE active.status = {(int)EmailDispatchStatus.DeadLettered}))::integer AS dead_count,
                            (COUNT(*) FILTER (WHERE active.status = {unknown}))::integer AS unknown_count,
                            (COUNT(*) FILTER (WHERE active.status = {(int)EmailDispatchStatus.Parked}))::integer AS parked_count,
                            MIN(active.created_at) FILTER (WHERE active.due) AS oldest
                        FROM active
                    ), backlog AS (
                        SELECT active.tenant_id, COUNT(*)::integer AS due_count
                        FROM active WHERE active.due GROUP BY active.tenant_id
                        ORDER BY COUNT(*) DESC, active.tenant_id LIMIT p_tenant_limit
                    )
                    SELECT totals.due_count, totals.retry_count, totals.stale_count, totals.dead_count,
                        totals.unknown_count, totals.parked_count, totals.oldest,
                        COALESCE(state.optional_reminders_deferred, FALSE),
                        COALESCE(state.is_paused, FALSE),
                        state.global_smtp_rate_limit_per_minute_override IS NOT NULL,
                        backlog.tenant_id, backlog.due_count
                    FROM totals
                    LEFT JOIN {states} AS state ON state.processor_code = 'smtp'
                    LEFT JOIN backlog ON TRUE
                    ORDER BY backlog.due_count DESC, backlog.tenant_id;
                END;
                $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {claim}(
                    p_lease uuid, p_claimed_at timestamp with time zone, p_take integer, p_per_tenant integer,
                    p_global_limit integer, p_tenant_limit integer, p_high integer, p_low integer,
                    p_tenant uuid, p_publish uuid)
                RETURNS TABLE (tenant_id uuid, outbox_id uuid)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE v_backlog bigint;
                BEGIN
                    IF p_lease IS NULL OR p_lease = '00000000-0000-0000-0000-000000000000'::uuid
                       OR p_claimed_at IS NULL OR NOT isfinite(p_claimed_at)
                       OR p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000
                       OR p_per_tenant IS NULL OR p_per_tenant NOT BETWEEN 1 AND 1000
                       OR p_global_limit IS NULL OR p_global_limit NOT BETWEEN 1 AND 256
                       OR p_tenant_limit IS NULL OR p_tenant_limit NOT BETWEEN 1 AND 256
                       OR p_high IS NULL OR p_high NOT BETWEEN 1 AND 1000000
                       OR p_low IS NULL OR p_low < 0 OR p_low >= p_high
                       OR (p_tenant IS NULL) <> (p_publish IS NULL)
                       OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                       OR p_publish = '00000000-0000-0000-0000-000000000000'::uuid
                       OR (p_tenant IS NOT NULL AND (p_take <> 1 OR p_per_tenant <> 1)) THEN
                        RAISE EXCEPTION 'invalid email admission bounds' USING ERRCODE = '22023';
                    END IF;
                    PERFORM pg_advisory_xact_lock({lockKey});
                    INSERT INTO {states} (id, processor_code, is_paused, optional_reminders_deferred, updated_at)
                    VALUES (p_lease, 'smtp', FALSE, FALSE, p_claimed_at)
                    ON CONFLICT (processor_code) DO NOTHING;
                    IF EXISTS (SELECT 1 FROM {states} AS state WHERE state.processor_code = 'smtp' AND state.is_paused) THEN
                        RETURN;
                    END IF;
                    RETURN QUERY SELECT queue.tenant_id, queue.id
                        FROM {outbox} AS queue
                        WHERE queue.status = {processing} AND queue.processing_lease_token = p_lease
                          AND queue.processing_started_at = p_claimed_at
                          AND (p_tenant IS NULL OR (queue.tenant_id = p_tenant AND queue.publish_event_id = p_publish))
                        ORDER BY queue.id LIMIT p_take;
                    IF FOUND THEN RETURN; END IF;
                    SELECT COUNT(*) INTO v_backlog
                    FROM {outbox} AS queue
                    LEFT JOIN {deliveries} AS delivery
                      ON delivery.tenant_id = queue.tenant_id AND delivery.email_dispatch_outbox_id = queue.id
                    WHERE queue.content_redacted_at IS NULL AND NOT queue.is_deleted
                      AND queue.status IN ({pending}, {retry})
                      AND (queue.next_attempt_at IS NULL OR queue.next_attempt_at <= p_claimed_at)
                      AND (queue.kind <> {reminder} OR COALESCE(delivery.is_required, FALSE))
                      AND NOT EXISTS (SELECT 1 FROM {controls} AS control
                          WHERE control.tenant_id = queue.tenant_id AND control.is_paused);
                    UPDATE {states} AS state
                    SET optional_reminders_deferred = CASE
                            WHEN v_backlog >= p_high THEN TRUE
                            WHEN v_backlog <= p_low THEN FALSE
                            ELSE state.optional_reminders_deferred END,
                        updated_at = p_claimed_at
                    WHERE state.processor_code = 'smtp';
                    RETURN QUERY
                    WITH active_by_tenant AS (
                        SELECT queue.tenant_id, COUNT(*) AS active_count
                        FROM {outbox} AS queue WHERE queue.status = {processing} GROUP BY queue.tenant_id
                    ), ranked AS (
                        SELECT queue.id, queue.tenant_id, queue.created_at,
                            CASE WHEN COALESCE(delivery.is_required, FALSE) THEN 0
                                 WHEN queue.kind = {reminder} THEN 2 ELSE 1 END AS priority,
                            ROW_NUMBER() OVER (PARTITION BY queue.tenant_id ORDER BY
                                CASE WHEN COALESCE(delivery.is_required, FALSE) THEN 0
                                     WHEN queue.kind = {reminder} THEN 2 ELSE 1 END,
                                queue.created_at, queue.id) AS tenant_rank
                        FROM {outbox} AS queue
                        LEFT JOIN {deliveries} AS delivery
                          ON delivery.tenant_id = queue.tenant_id AND delivery.email_dispatch_outbox_id = queue.id
                        CROSS JOIN {states} AS state
                        WHERE state.processor_code = 'smtp'
                          AND queue.content_redacted_at IS NULL AND NOT queue.is_deleted
                          AND queue.status IN ({pending}, {retry})
                          AND (queue.next_attempt_at IS NULL OR queue.next_attempt_at <= p_claimed_at)
                          AND (NOT state.optional_reminders_deferred OR queue.kind <> {reminder}
                               OR COALESCE(delivery.is_required, FALSE))
                          AND NOT EXISTS (SELECT 1 FROM {controls} AS control
                              WHERE control.tenant_id = queue.tenant_id AND control.is_paused)
                          AND (p_tenant IS NULL OR (queue.tenant_id = p_tenant AND queue.publish_event_id = p_publish))
                    ), candidates AS (
                        SELECT ranked.id FROM ranked
                        LEFT JOIN active_by_tenant AS active ON active.tenant_id = ranked.tenant_id
                        WHERE ranked.tenant_rank <= LEAST(p_per_tenant,
                            GREATEST(p_tenant_limit - COALESCE(active.active_count, 0), 0))
                        ORDER BY ranked.priority, ranked.tenant_rank, ranked.created_at, ranked.id
                        LIMIT GREATEST(LEAST(p_take, p_global_limit -
                            (SELECT COUNT(*) FROM {outbox} AS queue WHERE queue.status = {processing})), 0)
                    )
                    UPDATE {outbox} AS queue
                    SET status = {processing}, processing_started_at = p_claimed_at,
                        processing_lease_token = p_lease, updated_at = p_claimed_at
                    FROM candidates
                    WHERE queue.id = candidates.id AND queue.status IN ({pending}, {retry})
                    RETURNING queue.tenant_id, queue.id;
                END;
                $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {recovery}(
                    p_before timestamp with time zone, p_recovered timestamp with time zone, p_take integer,
                    p_retry_category text, p_retry_message text, p_unknown_category text, p_unknown_message text)
                RETURNS TABLE (retry_scheduled_count integer, unknown_count integer)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE v_row record; v_fenced boolean; v_retry integer := 0; v_unknown integer := 0;
                BEGIN
                    IF p_before IS NULL OR NOT isfinite(p_before)
                       OR p_recovered IS NULL OR NOT isfinite(p_recovered) OR p_before > p_recovered
                       OR p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000
                       OR p_retry_category IS NULL OR p_retry_message IS NULL
                       OR p_unknown_category IS NULL OR p_unknown_message IS NULL THEN
                        RAISE EXCEPTION 'invalid email recovery bounds' USING ERRCODE = '22023';
                    END IF;
                    PERFORM pg_advisory_xact_lock({lockKey});
                    FOR v_row IN
                        SELECT queue.id, queue.tenant_id, queue.attempt_count
                        FROM {outbox} AS queue
                        WHERE queue.status = {processing} AND queue.processing_started_at IS NOT NULL
                          AND queue.processing_started_at <= p_before
                        ORDER BY queue.processing_started_at, queue.id LIMIT p_take FOR UPDATE SKIP LOCKED
                    LOOP
                        v_fenced := EXISTS (SELECT 1 FROM {attempts} AS attempt
                            WHERE attempt.tenant_id = v_row.tenant_id AND attempt.email_dispatch_outbox_id = v_row.id
                              AND attempt.attempt_number = v_row.attempt_count AND attempt.failure_category = 'provider_handoff_started')
                            OR EXISTS (SELECT 1 FROM {receipts} AS receipt
                            WHERE receipt.tenant_id = v_row.tenant_id AND receipt.email_dispatch_outbox_id = v_row.id
                              AND receipt.status = {(int)EmailDispatchReceiptStatus.Processing});
                        IF v_fenced THEN
                            UPDATE {outbox} AS queue SET status = {unknown}, unknown_at = p_recovered,
                                next_attempt_at = NULL, processing_started_at = NULL, processing_lease_token = NULL,
                                last_failure_category = left(p_unknown_category, 100), last_error = left(p_unknown_message, 2000),
                                last_failure_at = p_recovered, updated_at = p_recovered
                            WHERE queue.tenant_id = v_row.tenant_id AND queue.id = v_row.id;
                            UPDATE {attempts} AS attempt SET outcome = {(int)EmailDispatchAttemptOutcome.Unknown},
                                completed_at = p_recovered, failure_category = left(p_unknown_category, 100),
                                sanitized_error_message = left(p_unknown_message, 2000),
                                provider_message_id = NULL, updated_at = p_recovered
                            WHERE attempt.tenant_id = v_row.tenant_id AND attempt.email_dispatch_outbox_id = v_row.id
                              AND attempt.attempt_number = v_row.attempt_count;
                            UPDATE {receipts} AS receipt SET status = {(int)EmailDispatchReceiptStatus.Unknown},
                                failed_at = p_recovered, failure_code = left(p_unknown_category, 100),
                                failure_message = left(p_unknown_message, 1000), updated_at = p_recovered
                            WHERE receipt.tenant_id = v_row.tenant_id AND receipt.email_dispatch_outbox_id = v_row.id
                              AND receipt.status = {(int)EmailDispatchReceiptStatus.Processing};
                            UPDATE {deliveries} AS delivery SET status_id = {(int)NotificationDeliveryStatusEnum.Unknown},
                                provider_message_id = NULL, provider_status = 'unknown', failure_category = left(p_unknown_category, 100),
                                completed_at = p_recovered, updated_at = p_recovered
                            WHERE delivery.tenant_id = v_row.tenant_id AND delivery.email_dispatch_outbox_id = v_row.id
                              AND delivery.channel_id = {(int)NotificationPreferenceChannelEnum.Email};
                            v_unknown := v_unknown + 1;
                        ELSE
                            UPDATE {outbox} AS queue SET status = {retry}, next_attempt_at = p_recovered,
                                processing_started_at = NULL, processing_lease_token = NULL,
                                last_failure_category = left(p_retry_category, 100), last_error = left(p_retry_message, 2000),
                                last_failure_at = p_recovered, updated_at = p_recovered
                            WHERE queue.tenant_id = v_row.tenant_id AND queue.id = v_row.id;
                            v_retry := v_retry + 1;
                        END IF;
                    END LOOP;
                    RETURN QUERY SELECT v_retry, v_unknown;
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {claimSignature}, {recoverySignature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {claimSignature}, {recoverySignature} TO {GlobalRole};
                    REVOKE ALL ON FUNCTION {healthSignature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {healthSignature} TO {GlobalRole};
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
