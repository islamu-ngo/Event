using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Security;

/// <summary>
/// Bounds Global fanout authority to atomic ledger admission and aggregate health.
/// The non-login definer cannot read occurrence snapshots, recipients, or run cursors.
/// </summary>
public static class PostgresNotificationFanoutAdmissionContract
{
    public const string OwnerRole = "event_fanout_admission_owner";
    public const string MigratorRole = "event_fanout_admission_migrator";
    public const string GlobalRole = "event_fanout_admission_global";
    private const string PolicyName = "notification_fanout_admission";

    public static string ClaimFunctionSql(ExploreDbContext context) => FunctionSql(context, "_admit");
    public static string SnapshotFunctionSql(ExploreDbContext context) => FunctionSql(context, "_snapshot");

    private static string FunctionSql(ExploreDbContext context, string suffix)
    {
        var entity = context.Model.FindEntityType(typeof(NotificationFanoutRun))!;
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

    internal sealed record AdmittedOwner(
        Guid RunId, Guid TenantId, Guid OccurrenceId, Guid LeaseToken, long Fence, int Generation);
    internal sealed record AdmittedRound(
        IReadOnlyList<AdmittedOwner> Owners, int CandidateCount, int LeaseContentionCount,
        int CapacityDeferredCount, int UnavailableCount);
    private sealed record AdmissionRow(
        Guid? RunId, Guid? TenantId, Guid? OccurrenceId, Guid? LeaseToken, long? Fence, int? Generation,
        int CandidateCount, int LeaseContentionCount, int CapacityDeferredCount, int UnavailableCount);

    internal static async Task<AdmittedRound> ClaimAsync(
        ExploreDbContext context, NotificationFanoutClaimRoundRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ClaimedAt.Kind != DateTimeKind.Utc || request.LeaseDuration <= TimeSpan.Zero
            || request.MaxTenants is < 1 or > 1000 || request.MaxActiveClaims <= 0
            || request.MaxActiveClaimsPerTenant <= 0
            || request.MaxActiveClaimsPerTenant > request.MaxActiveClaims
            || request.OptionalReminderBacklogHighWatermark <= 0
            || request.OptionalReminderBacklogLowWatermark < 0
            || request.OptionalReminderBacklogLowWatermark >= request.OptionalReminderBacklogHighWatermark
            || string.IsNullOrWhiteSpace(request.LeaseOwner))
            throw new ArgumentException("The notification fanout claim round is invalid.", nameof(request));
        string owner = request.LeaseOwner.Trim();
        if (owner.Length > NotificationFanoutRun.MaxLeaseOwnerLength)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A notification fanout claim round cannot run inside an ambient transaction.");

        string query = $"SELECT * FROM {ClaimFunctionSql(context)}" +
            "(@owner, @claimed, @duration, @take, @global_limit, @tenant_limit, @high, @low)";
        object[] parameters =
        [
            new NpgsqlParameter("owner", owner),
            new NpgsqlParameter("claimed", request.ClaimedAt),
            new NpgsqlParameter("duration", request.LeaseDuration),
            new NpgsqlParameter("take", request.MaxTenants),
            new NpgsqlParameter("global_limit", request.MaxActiveClaims),
            new NpgsqlParameter("tenant_limit", request.MaxActiveClaimsPerTenant),
            new NpgsqlParameter("high", request.OptionalReminderBacklogHighWatermark),
            new NpgsqlParameter("low", request.OptionalReminderBacklogLowWatermark)
        ];
        List<AdmissionRow> rows = await context.Database.SqlQueryRaw<AdmissionRow>(query, parameters)
            .ToListAsync(cancellationToken);
        AdmissionRow summary = rows[0];
        AdmittedOwner[] owners = rows.Where(row => row.RunId.HasValue)
            .Select(row => new AdmittedOwner(row.RunId!.Value, row.TenantId!.Value, row.OccurrenceId!.Value,
                row.LeaseToken!.Value, row.Fence!.Value, row.Generation!.Value)).ToArray();
        return new AdmittedRound(owners, summary.CandidateCount, summary.LeaseContentionCount,
            summary.CapacityDeferredCount, summary.UnavailableCount);
    }

    internal static Task<NotificationFanoutProcessorSnapshot> SnapshotAsync(
        ExploreDbContext context, DateTime observedAt, CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The fanout processor observation time must be UTC.", nameof(observedAt));
        return context.Database.SqlQueryRaw<NotificationFanoutProcessorSnapshot>(
            $"SELECT * FROM {SnapshotFunctionSql(context)}({{0}})", observedAt).SingleAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var entity = context.Model.FindEntityType(typeof(NotificationFanoutRun))!;
        string schema = sql.DelimitIdentifier(Schema(context, entity));
        string Table<T>()
        {
            var mapping = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(mapping.GetTableName()!, Schema(context, mapping));
        }
        string runs = Table<NotificationFanoutRun>();
        string occurrences = Table<NotificationFanoutOccurrence>();
        string states = Table<NotificationFanoutProcessorState>();
        string events = Table<Explore.Domain.Event>();
        string claim = ClaimFunctionSql(context);
        string snapshot = SnapshotFunctionSql(context);
        string claimSignature = $"{claim}(text, timestamp with time zone, interval, integer, integer, integer, integer, integer)";
        string snapshotSignature = $"{snapshot}(timestamp with time zone)";
        long globalLock = RelationalNamedLock.ComputeStableKey("notification-fanout-global-claim");
        int reminder = (int)NotificationDeliveryPolicyEnum.ReminderOptional;
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
                    RAISE EXCEPTION 'fanout admission global and migration authority must remain separate';
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
                    RAISE EXCEPTION 'fanout admission global and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {runs}, {occurrences}, {states}, {events} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT (id, tenant_id, fanout_occurrence_id, status, processing_lease_owner,
                processing_lease_token, processing_lease_expires_at, processing_generation, processing_fence,
                started_at, processed_count) ON {runs} TO {OwnerRole};
            GRANT UPDATE (status, processing_lease_owner, processing_lease_token, processing_lease_expires_at,
                processing_generation, processing_fence, heartbeat_at, started_at, failed_at, last_error,
                updated_at, concurrency_stamp) ON {runs} TO {OwnerRole};
            GRANT SELECT (id, tenant_id, event_id, state, not_before, delivery_policy_id, priority, occurred_at)
                ON {occurrences} TO {OwnerRole};
            GRANT SELECT (id, tenant_id, is_deleted) ON {events} TO {OwnerRole};
            GRANT SELECT (processor_code, optional_reminders_deferred) ON {states} TO {OwnerRole};
            GRANT INSERT (id, processor_code, optional_reminders_deferred, updated_at) ON {states} TO {OwnerRole};
            GRANT UPDATE (optional_reminders_deferred, updated_at) ON {states} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {runs};
            CREATE POLICY "{PolicyName}" ON {runs} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {occurrences};
            CREATE POLICY "{PolicyName}" ON {occurrences} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {events};
            CREATE POLICY "{PolicyName}" ON {events} FOR SELECT TO {OwnerRole} USING (true);
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {claim}(
                    p_owner text, p_claimed timestamp with time zone, p_duration interval,
                    p_take integer, p_global_limit integer, p_tenant_limit integer, p_high integer, p_low integer)
                RETURNS TABLE ("RunId" uuid, "TenantId" uuid, "OccurrenceId" uuid, "LeaseToken" uuid,
                    "Fence" bigint, "Generation" integer, "CandidateCount" integer,
                    "LeaseContentionCount" integer, "CapacityDeferredCount" integer, "UnavailableCount" integer)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE
                    v_observed timestamptz := LEAST(p_claimed, statement_timestamp());
                    v_backlog bigint; v_active bigint; v_tenant_active bigint; v_deferred boolean;
                    v_candidate record; v_run record; v_pending boolean;
                    v_claimed uuid[] := ARRAY[]::uuid[];
                    v_candidates integer := 0; v_contention integer := 0;
                    v_capacity integer := 0; v_unavailable integer := 0;
                    v_resource text;
                BEGIN
                    IF p_owner IS NULL OR length(btrim(p_owner)) NOT BETWEEN 1 AND {NotificationFanoutRun.MaxLeaseOwnerLength}
                       OR p_claimed IS NULL OR NOT isfinite(p_claimed)
                       OR p_duration IS NULL OR NOT isfinite(p_duration) OR p_duration <= interval '0 seconds'
                       OR p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000
                       OR p_global_limit IS NULL OR p_global_limit <= 0
                       OR p_tenant_limit IS NULL OR p_tenant_limit <= 0 OR p_tenant_limit > p_global_limit
                       OR p_high IS NULL OR p_high <= 0 OR p_low IS NULL OR p_low < 0 OR p_low >= p_high THEN
                        RAISE EXCEPTION 'invalid fanout admission bounds' USING ERRCODE = '22023';
                    END IF;
                    PERFORM pg_advisory_xact_lock({globalLock});
                    INSERT INTO {states} (id, processor_code, optional_reminders_deferred, updated_at)
                    VALUES (uuidv7(), 'fanout', FALSE, v_observed) ON CONFLICT (processor_code) DO NOTHING;
                    SELECT COUNT(*) INTO v_backlog
                    FROM {occurrences} AS occurrence
                    INNER JOIN {runs} AS run ON run.tenant_id = occurrence.tenant_id
                        AND run.fanout_occurrence_id = occurrence.id
                    WHERE occurrence.state = 1 AND occurrence.not_before <= v_observed
                        AND occurrence.delivery_policy_id <> {reminder} AND run.status IN ('pending', 'processing');
                    UPDATE {states} AS state SET optional_reminders_deferred =
                        v_backlog >= p_high OR (v_backlog > p_low AND state.optional_reminders_deferred),
                        updated_at = v_observed WHERE state.processor_code = 'fanout';
                    SELECT state.optional_reminders_deferred INTO v_deferred
                        FROM {states} AS state WHERE state.processor_code = 'fanout';
                    SELECT COUNT(*) INTO v_active FROM {runs} AS run
                        WHERE run.fanout_occurrence_id IS NOT NULL AND run.status = 'processing'
                            AND run.processing_lease_expires_at > v_observed;
                    FOR v_candidate IN
                        WITH active_by_tenant AS (
                            SELECT run.tenant_id, COUNT(*) AS active_count FROM {runs} AS run
                            WHERE run.fanout_occurrence_id IS NOT NULL AND run.status = 'processing'
                                AND run.processing_lease_expires_at > v_observed GROUP BY run.tenant_id
                        ), ranked AS (
                            SELECT occurrence.tenant_id, occurrence.id, occurrence.event_id,
                                occurrence.priority, occurrence.occurred_at,
                                ROW_NUMBER() OVER (PARTITION BY occurrence.tenant_id
                                    ORDER BY occurrence.priority DESC, occurrence.occurred_at, occurrence.id) AS tenant_rank
                            FROM {occurrences} AS occurrence
                            INNER JOIN {runs} AS run ON run.tenant_id = occurrence.tenant_id
                                AND run.fanout_occurrence_id = occurrence.id
                            LEFT JOIN active_by_tenant AS active ON active.tenant_id = occurrence.tenant_id
                            WHERE occurrence.state = 1 AND occurrence.not_before <= v_observed
                                AND COALESCE(active.active_count, 0) < p_tenant_limit
                                AND (NOT v_deferred OR occurrence.delivery_policy_id <> {reminder})
                                AND (run.status = 'pending' OR
                                    (run.status = 'processing' AND run.processing_lease_expires_at <= v_observed))
                        )
                        SELECT ranked.tenant_id, ranked.id, ranked.event_id FROM ranked
                        WHERE ranked.tenant_rank = 1
                        ORDER BY ranked.priority DESC, ranked.occurred_at, ranked.id
                        LIMIT LEAST(p_take, GREATEST(0, p_global_limit - v_active))
                    LOOP
                        v_candidates := v_candidates + 1;
                        FOREACH v_resource IN ARRAY ARRAY[
                            'notification-fanout-tenant-claim:' || replace(v_candidate.tenant_id::text, '-', ''),
                            'notification-fanout-precedence:' || replace(v_candidate.tenant_id::text, '-', '')
                                || ':' || replace(v_candidate.event_id::text, '-', ''),
                            'notification-fanout:' || replace(v_candidate.tenant_id::text, '-', '')
                                || ':' || replace(v_candidate.id::text, '-', '')] LOOP
                            PERFORM pg_advisory_xact_lock(('x' ||
                                substr(encode(sha256(convert_to(v_resource, 'UTF8')), 'hex'), 1, 16))::bit(64)::bigint);
                        END LOOP;
                        SELECT EXISTS (
                            SELECT 1 FROM {occurrences} AS occurrence INNER JOIN {events} AS event
                                ON event.tenant_id = occurrence.tenant_id AND event.id = occurrence.event_id
                            WHERE occurrence.tenant_id = v_candidate.tenant_id AND occurrence.id = v_candidate.id
                                AND occurrence.event_id = v_candidate.event_id AND occurrence.state = 1
                                AND occurrence.not_before <= v_observed AND NOT event.is_deleted
                        ) INTO v_pending;
                        IF NOT v_pending THEN v_unavailable := v_unavailable + 1; CONTINUE; END IF;
                        SELECT run.id, run.status, run.processing_lease_expires_at INTO v_run FROM {runs} AS run
                            WHERE run.tenant_id = v_candidate.tenant_id AND run.fanout_occurrence_id = v_candidate.id
                            FOR UPDATE;
                        IF NOT FOUND THEN v_unavailable := v_unavailable + 1; CONTINUE; END IF;
                        IF v_run.status = 'processing' AND v_run.processing_lease_expires_at > v_observed THEN
                            v_contention := v_contention + 1; CONTINUE;
                        END IF;
                        IF v_run.status <> 'pending' AND NOT
                            (v_run.status = 'processing' AND v_run.processing_lease_expires_at <= v_observed) THEN
                            v_unavailable := v_unavailable + 1; CONTINUE;
                        END IF;
                        SELECT COUNT(*) INTO v_tenant_active FROM {runs} AS run
                            WHERE run.tenant_id = v_candidate.tenant_id AND run.fanout_occurrence_id IS NOT NULL
                                AND run.status = 'processing' AND run.processing_lease_expires_at > v_observed;
                        IF v_active >= p_global_limit OR v_tenant_active >= p_tenant_limit THEN
                            v_capacity := v_capacity + 1; CONTINUE;
                        END IF;
                        UPDATE {runs} AS run SET status = 'processing', processing_lease_owner = btrim(p_owner),
                            processing_lease_token = uuidv7(), processing_lease_expires_at = v_observed + p_duration,
                            heartbeat_at = v_observed, started_at = COALESCE(run.started_at, v_observed),
                            processing_generation = run.processing_generation + 1,
                            processing_fence = run.processing_fence + 1, failed_at = NULL, last_error = NULL,
                            updated_at = v_observed, concurrency_stamp = uuidv7()
                            WHERE run.id = v_run.id AND run.tenant_id = v_candidate.tenant_id;
                        v_claimed := array_append(v_claimed, v_run.id);
                        v_active := v_active + 1;
                    END LOOP;
                    RETURN QUERY
                        SELECT run.id, run.tenant_id, run.fanout_occurrence_id, run.processing_lease_token,
                            run.processing_fence, run.processing_generation, v_candidates, v_contention,
                            v_capacity, v_unavailable FROM {runs} AS run
                        INNER JOIN {occurrences} AS occurrence ON occurrence.tenant_id = run.tenant_id
                            AND occurrence.id = run.fanout_occurrence_id
                        WHERE run.id = ANY(v_claimed)
                        ORDER BY occurrence.priority DESC, occurrence.occurred_at, occurrence.id;
                    IF cardinality(v_claimed) = 0 THEN
                        RETURN QUERY SELECT NULL::uuid, NULL::uuid, NULL::uuid, NULL::uuid, NULL::bigint,
                            NULL::integer, v_candidates, v_contention, v_capacity, v_unavailable;
                    END IF;
                END;
                $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {snapshot}(p_observed timestamp with time zone)
                RETURNS TABLE ("DueOccurrenceCount" integer, "DueCoreOccurrenceCount" integer,
                    "DueOptionalReminderCount" integer, "ActiveClaimCount" integer, "ExpiredClaimCount" integer,
                    "SupersededOccurrenceCount" integer, "ProcessedRecipientCount" bigint,
                    "OldestDueAt" timestamp with time zone, "OptionalRemindersDeferred" boolean)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE v_observed timestamptz := LEAST(p_observed, statement_timestamp());
                BEGIN
                    IF p_observed IS NULL OR NOT isfinite(p_observed) THEN
                        RAISE EXCEPTION 'invalid fanout observation time' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY
                    WITH due AS (
                        SELECT occurrence.delivery_policy_id, occurrence.occurred_at
                        FROM {occurrences} AS occurrence INNER JOIN {runs} AS run
                            ON run.tenant_id = occurrence.tenant_id AND run.fanout_occurrence_id = occurrence.id
                        WHERE occurrence.state = 1 AND occurrence.not_before <= v_observed
                            AND (run.status = 'pending' OR
                                (run.status = 'processing' AND run.processing_lease_expires_at <= v_observed))
                    )
                    SELECT (SELECT COUNT(*)::integer FROM due),
                        (SELECT COUNT(*)::integer FROM due WHERE delivery_policy_id <> {reminder}),
                        (SELECT COUNT(*)::integer FROM due WHERE delivery_policy_id = {reminder}),
                        (SELECT COUNT(*)::integer FROM {runs} AS run WHERE run.fanout_occurrence_id IS NOT NULL
                            AND run.status = 'processing' AND run.processing_lease_expires_at > v_observed),
                        (SELECT COUNT(*)::integer FROM {runs} AS run WHERE run.fanout_occurrence_id IS NOT NULL
                            AND run.status = 'processing' AND run.processing_lease_expires_at <= v_observed),
                        (SELECT COUNT(*)::integer FROM {occurrences} AS occurrence WHERE occurrence.state = 2),
                        (SELECT COALESCE(SUM(run.processed_count), 0)::bigint FROM {runs} AS run
                            WHERE run.fanout_occurrence_id IS NOT NULL AND run.status IN ('pending', 'processing')),
                        (SELECT MIN(occurred_at) FROM due),
                        COALESCE((SELECT state.optional_reminders_deferred FROM {states} AS state
                            WHERE state.processor_code = 'fanout'), FALSE);
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {claimSignature}, {snapshotSignature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {claimSignature}, {snapshotSignature} TO {GlobalRole};
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
