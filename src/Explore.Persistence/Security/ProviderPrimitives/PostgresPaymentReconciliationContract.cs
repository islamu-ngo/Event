using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Migration-owned claim and aggregate capability. Global receives ownership only;
/// the non-login definer cannot read payment amounts, recipients or idempotency keys.
/// Economic settlement remains under ordinary immutable tenant authority.
/// </summary>
public static class PostgresPaymentReconciliationContract
{
    public const string OwnerRole = "event_payment_reconciliation_owner";
    public const string MigratorRole = "event_payment_reconciliation_migrator";
    public const string GlobalRole = "event_payment_reconciliation_global";
    public const string PolicyName = "payment_reconciliation_admission";

    public static string ClaimFunctionSql(ExploreDbContext context) => FunctionSql(context, "_claim");
    public static string SnapshotFunctionSql(ExploreDbContext context) => FunctionSql(context, "_snapshot");

    private static string FunctionSql(ExploreDbContext context, string suffix) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(PaymentReconciliationEffect))!.GetTableName()! + suffix, Schema(context));

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(PaymentReconciliationEffect))!.GetSchema() ?? "public";

    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        enrollment.Validate();
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<PaymentReconciliationClaim>> ClaimAsync(
        ExploreDbContext context, PaymentReconciliationAcquisition acquisition, DateTime observedAt,
        bool recoverOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!acquisition.IsValidAt(observedAt) || acquisition.LeaseExpiresAt <= observedAt)
            return [];
        List<PaymentReconciliationClaim> claims = await context.Database.SqlQueryRaw<PaymentReconciliationClaim>(
            $$"""
            SELECT "TenantId" AS tenant_id, "EffectId" AS effect_id,
                "PaymentAttemptId" AS payment_attempt_id, "LeaseToken" AS lease_token,
                "ProcessingFence" AS processing_fence, "AttemptCount" AS attempt_count,
                "CheckoutDispatchEffectId" AS checkout_dispatch_effect_id,
                "CheckoutDispatchUnknownAt" AS checkout_dispatch_unknown_at,
                "CheckoutDispatchProcessingFence" AS checkout_dispatch_processing_fence,
                "CheckoutDispatchAttemptCount" AS checkout_dispatch_attempt_count
            FROM {{ClaimFunctionSql(context)}}({0}, {1}, {2}, {3}, {4}, {5}, {6})
            """,
            acquisition.LeaseToken, acquisition.LeaseOwner, acquisition.BatchSize, acquisition.ClaimedAt,
            acquisition.LeaseExpiresAt, observedAt, recoverOnly).ToListAsync(cancellationToken);
        return claims.OrderBy(value => value.EffectId).ToArray();
    }

    internal static Task<PaymentReconciliationHealth> SnapshotAsync(
        ExploreDbContext context, DateTime observedAt, CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Payment reconciliation health requires a UTC observation.", nameof(observedAt));
        return context.Database.SqlQueryRaw<PaymentReconciliationHealth>(
            $$"""
            SELECT "Due" AS due, "Unknown" AS unknown, "Parked" AS parked,
                "OldestDueAt" AS oldest_due_at, "ConfigurationBlocked" AS configuration_blocked,
                "DuplicateSucceededOrders" AS duplicate_succeeded_orders
            FROM {{SnapshotFunctionSql(context)}}({0})
            """, observedAt).SingleAsync(cancellationToken);
    }

    private static PostgresPrivilegeGuard.Contract Privileges(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string Name<TEntity>() where TEntity : class => context.Model.FindEntityType(typeof(TEntity))!.GetTableName()!;
        string[] read =
        [
            "tenant_id", "id", "payment_attempt_id", "status", "next_attempt_at", "created_at",
            "processing_lease_expires_at", "processing_lease_token", "processing_fence", "attempt_count",
            "checkout_dispatch_effect_id", "checkout_dispatch_unknown_at", "checkout_dispatch_processing_fence",
            "checkout_dispatch_attempt_count", "last_failure_code", "unknown_at", "processing_lease_owner", "updated_at"
        ];
        string[] write =
        [
            "status", "next_attempt_at", "processing_lease_owner", "processing_lease_token",
            "processing_lease_expires_at", "processing_fence", "attempt_count", "last_failure_code", "unknown_at", "updated_at"
        ];
        return new(Schema(context), [OwnerRole, MigratorRole, GlobalRole],
            [
                new(Name<PaymentReconciliationEffect>() + "_claim",
                    "uuid,text,integer,timestamp with time zone,timestamp with time zone,timestamp with time zone,boolean",
                    OwnerRole, [GlobalRole]),
                new(Name<PaymentReconciliationEffect>() + "_snapshot", "timestamp with time zone", OwnerRole, [GlobalRole])
            ],
            [
                new(Name<PaymentReconciliationEffect>(), OwnerRole, [],
                    read.Select(name => new PostgresPrivilegeGuard.Column(name,
                        write.Contains(name, StringComparer.Ordinal) ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray()),
                new(Name<CheckoutDispatchEffect>(), OwnerRole, [],
                    [new("status", ["SELECT"]), new("last_failure_code", ["SELECT"])]),
                new(Name<PaymentSucceededObservation>(), OwnerRole, [],
                    [new("tenant_id", ["SELECT"]), new("registration_order_id", ["SELECT"])])
            ],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var contract = Privileges(context, enrollment);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context));
        string Table<TEntity>() where TEntity : class =>
            sql.DelimitIdentifier(context.Model.FindEntityType(typeof(TEntity))!.GetTableName()!, Schema(context));
        string effects = Table<PaymentReconciliationEffect>();
        string dispatches = Table<CheckoutDispatchEffect>();
        string observations = Table<PaymentSucceededObservation>();
        string claim = ClaimFunctionSql(context);
        string snapshot = SnapshotFunctionSql(context);
        string claimSignature = $"{claim}(uuid, text, integer, timestamp with time zone, timestamp with time zone, timestamp with time zone, boolean)";
        string snapshotSignature = $"{snapshot}(timestamp with time zone)";
        int pending = (int)OutboxMessageStatus.Pending;
        int failed = (int)OutboxMessageStatus.Failed;
        int processing = (int)OutboxMessageStatus.Processing;
        int parked = (int)OutboxMessageStatus.DeadLettered;
        return $"""
            DO $contract$
            DECLARE v_role text;
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {Text(enrollment.GlobalLogin)}
                        AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                        AND NOT rolcreaterole AND NOT rolreplication) THEN
                    RAISE EXCEPTION 'payment_reconciliation_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')) THEN
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = session_user AND rolsuper) THEN
                        RAISE EXCEPTION 'payment_reconciliation_administrator_bootstrap_required' USING ERRCODE = '55000';
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                        WHERE namespace.nspname = {Text(Schema(context))}
                            AND routine.proname IN ({string.Join(", ", contract.Routines.Select(routine => Text(routine.Name)))}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                            JOIN pg_catalog.pg_class relation ON relation.oid = policy.polrelid
                            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                            WHERE namespace.nspname = {Text(Schema(context))} AND policy.polname = '{PolicyName}') THEN
                        RAISE EXCEPTION 'payment_reconciliation_partial_bootstrap' USING ERRCODE = '55000';
                    END IF;
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END LOOP;
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT {GlobalRole} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {Text(enrollment.GlobalLogin)});
                    EXECUTE format('GRANT {MigratorRole} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {Text(enrollment.MigratorLogin)});
                ELSE
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)} THEN
                        RAISE EXCEPTION 'payment_reconciliation_enrollment_invalid' USING ERRCODE = '55000';
                    END IF;
                    {guard}
                END IF;
            END $contract$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {effects}, {dispatches}, {observations} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT (tenant_id, id, payment_attempt_id, status, next_attempt_at, created_at,
                processing_lease_expires_at, processing_lease_token, processing_fence, attempt_count,
                checkout_dispatch_effect_id, checkout_dispatch_unknown_at, checkout_dispatch_processing_fence,
                checkout_dispatch_attempt_count, last_failure_code, unknown_at,
                processing_lease_owner, updated_at) ON {effects} TO {OwnerRole};
            GRANT UPDATE (status, next_attempt_at, processing_lease_owner, processing_lease_token,
                processing_lease_expires_at, processing_fence, attempt_count, last_failure_code,
                unknown_at, updated_at) ON {effects} TO {OwnerRole};
            GRANT SELECT (status, last_failure_code) ON {dispatches} TO {OwnerRole};
            GRANT SELECT (tenant_id, registration_order_id) ON {observations} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {effects};
            CREATE POLICY "{PolicyName}" ON {effects} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {dispatches};
            CREATE POLICY "{PolicyName}" ON {dispatches} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {observations};
            CREATE POLICY "{PolicyName}" ON {observations} FOR SELECT TO {OwnerRole} USING (true);
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {claim}(
                    p_token uuid, p_owner text, p_take integer, p_claimed timestamp with time zone,
                    p_expires timestamp with time zone, p_observed timestamp with time zone, p_recover_only boolean)
                RETURNS TABLE ("TenantId" uuid, "EffectId" uuid, "PaymentAttemptId" uuid, "LeaseToken" uuid,
                    "ProcessingFence" bigint, "AttemptCount" integer, "CheckoutDispatchEffectId" uuid,
                    "CheckoutDispatchUnknownAt" timestamp with time zone, "CheckoutDispatchProcessingFence" bigint,
                    "CheckoutDispatchAttemptCount" integer)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                            WHERE (pg_catalog.pg_has_role(session_user, role.oid, 'USAGE')
                                OR pg_catalog.pg_has_role(session_user, role.oid, 'SET'))
                                AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                                    OR role.rolcreaterole OR role.rolreplication)) THEN
                        RAISE EXCEPTION 'payment_reconciliation_global_login_required' USING ERRCODE = '42501';
                    END IF;
                    IF p_token IS NULL OR p_token = '00000000-0000-0000-0000-000000000000'::uuid
                       OR p_owner IS NULL OR p_owner <> btrim(p_owner)
                       OR length(p_owner) NOT BETWEEN 1 AND {PaymentReconciliationEffect.MaxLeaseOwnerLength}
                       OR p_take IS NULL OR p_take NOT BETWEEN 1 AND 50
                       OR p_claimed IS NULL OR NOT isfinite(p_claimed)
                       OR p_expires IS NULL OR NOT isfinite(p_expires) OR p_expires <= p_claimed
                       OR p_observed IS NULL OR NOT isfinite(p_observed) OR p_observed < p_claimed
                       OR p_recover_only IS NULL THEN
                        RAISE EXCEPTION 'invalid payment reconciliation claim bounds' USING ERRCODE = '22023';
                    END IF;
                    IF p_expires <= p_observed THEN RETURN; END IF;
                    IF EXISTS (SELECT 1 FROM {effects} AS queue WHERE queue.processing_lease_token = p_token) THEN
                        IF (SELECT count(*) FROM {effects} AS queue WHERE queue.processing_lease_token = p_token) > p_take
                           OR EXISTS (
                               SELECT 1 FROM {effects} AS queue WHERE queue.processing_lease_token = p_token
                                 AND (queue.status <> {processing}
                                   OR queue.processing_lease_owner IS DISTINCT FROM p_owner
                                   OR queue.updated_at IS DISTINCT FROM p_claimed
                                   OR queue.processing_lease_expires_at IS DISTINCT FROM p_expires
                                   OR queue.tenant_id = '00000000-0000-0000-0000-000000000000'::uuid
                                   OR queue.processing_fence <= 0 OR queue.attempt_count <= 0)) THEN
                            RETURN;
                        END IF;
                        RETURN QUERY SELECT queue.tenant_id, queue.id, queue.payment_attempt_id,
                            queue.processing_lease_token, queue.processing_fence, queue.attempt_count,
                            queue.checkout_dispatch_effect_id, queue.checkout_dispatch_unknown_at,
                            queue.checkout_dispatch_processing_fence, queue.checkout_dispatch_attempt_count
                        FROM {effects} AS queue
                        WHERE queue.processing_lease_token = p_token
                          AND queue.status = {processing} AND queue.processing_lease_owner = p_owner
                          AND queue.updated_at = p_claimed AND queue.processing_lease_expires_at = p_expires
                          AND queue.processing_lease_expires_at > p_observed
                        ORDER BY queue.id LIMIT p_take;
                        RETURN;
                    END IF;
                    IF p_recover_only THEN RETURN; END IF;
                    RETURN QUERY
                    WITH locked AS (
                        SELECT queue.tenant_id, queue.id, coalesce(queue.next_attempt_at, queue.created_at) AS due_at,
                            queue.created_at
                        FROM {effects} AS queue
                        WHERE (queue.status IN ({pending}, {failed}) AND queue.next_attempt_at <= p_claimed)
                           OR (queue.status = {processing} AND queue.processing_lease_expires_at <= p_claimed)
                        ORDER BY coalesce(queue.next_attempt_at, queue.created_at), queue.created_at, queue.id
                        LIMIT p_take FOR UPDATE SKIP LOCKED
                    ), candidates AS (
                        SELECT locked.tenant_id, locked.id,
                            row_number() OVER (ORDER BY locked.due_at, locked.created_at, locked.id) AS claim_order
                        FROM locked
                    ), updated AS (
                        UPDATE {effects} AS effect
                        SET status = {processing},
                            attempt_count = effect.attempt_count + 1,
                            processing_fence = effect.processing_fence + 1,
                            processing_lease_owner = p_owner,
                            processing_lease_token = p_token,
                            processing_lease_expires_at = p_expires,
                            next_attempt_at = NULL,
                            last_failure_code = CASE WHEN effect.status = {processing}
                                THEN 'payment_reconciliation_interrupted' ELSE effect.last_failure_code END,
                            unknown_at = CASE WHEN effect.status = {processing} THEN p_claimed ELSE effect.unknown_at END,
                            updated_at = p_claimed
                        FROM candidates
                        WHERE effect.tenant_id = candidates.tenant_id AND effect.id = candidates.id
                        RETURNING effect.tenant_id, effect.id, effect.payment_attempt_id, effect.processing_lease_token,
                            effect.processing_fence, effect.attempt_count, effect.checkout_dispatch_effect_id,
                            effect.checkout_dispatch_unknown_at, effect.checkout_dispatch_processing_fence,
                            effect.checkout_dispatch_attempt_count, candidates.claim_order
                    )
                    SELECT updated.tenant_id, updated.id, updated.payment_attempt_id, updated.processing_lease_token,
                        updated.processing_fence, updated.attempt_count, updated.checkout_dispatch_effect_id,
                        updated.checkout_dispatch_unknown_at, updated.checkout_dispatch_processing_fence,
                        updated.checkout_dispatch_attempt_count FROM updated ORDER BY updated.claim_order;
                END $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {snapshot}(p_observed timestamp with time zone)
                RETURNS TABLE ("Due" integer, "Unknown" integer, "Parked" integer, "OldestDueAt" timestamp with time zone,
                    "ConfigurationBlocked" integer, "DuplicateSucceededOrders" integer)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                            WHERE (pg_catalog.pg_has_role(session_user, role.oid, 'USAGE')
                                OR pg_catalog.pg_has_role(session_user, role.oid, 'SET'))
                                AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                                    OR role.rolcreaterole OR role.rolreplication)) THEN
                        RAISE EXCEPTION 'payment_reconciliation_global_login_required' USING ERRCODE = '42501';
                    END IF;
                    IF p_observed IS NULL OR NOT isfinite(p_observed) THEN
                        RAISE EXCEPTION 'invalid payment reconciliation health observation' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY SELECT
                        count(*) FILTER (WHERE
                            (queue.status IN ({pending}, {failed}) AND queue.next_attempt_at <= p_observed)
                            OR (queue.status = {processing} AND queue.processing_lease_expires_at <= p_observed))::integer,
                        count(*) FILTER (WHERE queue.status = {failed} AND queue.unknown_at IS NOT NULL)::integer,
                        count(*) FILTER (WHERE queue.status = {parked})::integer,
                        min(queue.next_attempt_at) FILTER (
                            WHERE queue.status IN ({pending}, {failed}) AND queue.next_attempt_at <= p_observed),
                        (count(*) FILTER (WHERE queue.status = {failed}
                            AND left(queue.last_failure_code, length('checkout_provider_')) = 'checkout_provider_')
                            + (SELECT count(*) FROM {dispatches} AS dispatch WHERE dispatch.status = {failed}
                                AND left(dispatch.last_failure_code, length('checkout_provider_')) = 'checkout_provider_'))::integer,
                        (SELECT count(*)::integer FROM (
                            SELECT observation.tenant_id, observation.registration_order_id FROM {observations} AS observation
                            GROUP BY observation.tenant_id, observation.registration_order_id HAVING count(*) > 1) AS duplicates)
                    FROM {effects} AS queue;
                END $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {claimSignature}, {snapshotSignature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {claimSignature}, {snapshotSignature} TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $contract$
            BEGIN
                {guard}
                IF pg_catalog.has_schema_privilege('{OwnerRole}', '{Schema(context).Replace("'", "''", StringComparison.Ordinal)}', 'CREATE') THEN
                    RAISE EXCEPTION 'payment owner retains effective schema creation authority' USING ERRCODE = '42501';
                END IF;
            END $contract$;
            """;
    }
}
