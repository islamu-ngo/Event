using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Global receives only checkout claim identities. The non-login definer has column-bounded
/// queue authority and no payment, recipient, routing or provider-idempotency-key access.
/// </summary>
public static class PostgresCheckoutDispatchContract
{
    public const string OwnerRole = "event_checkout_dispatch_owner";
    public const string MigratorRole = "event_checkout_dispatch_migrator";
    public const string GlobalRole = "event_checkout_dispatch_global";
    public const string PolicyName = "checkout_dispatch_admission";

    // PostgreSQL drops sub-microsecond ticks when binding a timestamp parameter.
    // Reject a changed deadline before truncation could equal the original lease.
    internal static bool HasCanonicalLease(ExploreDbContext context, CheckoutDispatchClaim claim) =>
        !context.Database.IsNpgsql()
        || claim.LeaseExpiresAt.Kind == DateTimeKind.Utc && claim.LeaseExpiresAt.Ticks % 10 == 0;

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(CheckoutDispatchEffect))!.GetSchema() ?? "public";

    public static string ClaimFunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(CheckoutDispatchEffect))!.GetTableName()! + "_claim", Schema(context));

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

    internal static async Task<IReadOnlyList<CheckoutDispatchClaim>> ClaimAsync(
        ExploreDbContext context, string owner, DateTime claimedAt, DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        Guid token = Guid.CreateVersion7();
        // A lost acknowledgement leaves the original lease to expire. Do not acquire
        // replacement ownership or infer replay provenance from an already Processing row.
        return await context.Database.SqlQueryRaw<CheckoutDispatchClaim>(
            $$"""
            SELECT "EffectId" AS effect_id, "TenantId" AS tenant_id,
                "RegistrationOrderId" AS registration_order_id, "PaymentAttemptId" AS payment_attempt_id,
                "LeaseToken" AS lease_token, "ProcessingFence" AS processing_fence,
                "LeaseExpiresAt" AS lease_expires_at, "ReplayKind" AS replay_kind, "AttemptCount" AS attempt_count
            FROM {{ClaimFunctionSql(context)}}({0}, {1}, {2}, {3})
            """,
            token, owner, claimedAt, expiresAt).ToListAsync(cancellationToken);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private static PostgresPrivilegeGuard.Contract Privileges(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string table = context.Model.FindEntityType(typeof(CheckoutDispatchEffect))!.GetTableName()!;
        string[] read =
        [
            "id", "tenant_id", "registration_order_id", "payment_attempt_id", "status", "next_attempt_at",
            "created_at", "processing_lease_token", "processing_lease_expires_at", "processing_fence",
            "attempt_count", "unknown_at", "last_failure_code", "processing_lease_owner", "updated_at"
        ];
        string[] write =
        [
            "status", "next_attempt_at", "processing_lease_owner", "processing_lease_token",
            "processing_lease_expires_at", "processing_fence", "attempt_count", "updated_at"
        ];
        return new(Schema(context), [OwnerRole, MigratorRole, GlobalRole],
            [new(table + "_claim", "uuid,text,timestamp with time zone,timestamp with time zone", OwnerRole, [GlobalRole])],
            [new(table, OwnerRole, [], read.Select(column => new PostgresPrivilegeGuard.Column(column,
                write.Contains(column, StringComparer.Ordinal) ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray())],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var contract = Privileges(context, enrollment);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context));
        string effects = sql.DelimitIdentifier(context.Model.FindEntityType(typeof(CheckoutDispatchEffect))!.GetTableName()!, Schema(context));
        string claim = ClaimFunctionSql(context);
        string signature = $"{claim}(uuid, text, timestamp with time zone, timestamp with time zone)";
        string sourceGuard = $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                WHERE relation.oid = {Text(effects)}::regclass
                    AND relation.relrowsecurity AND relation.relforcerowsecurity) THEN
                RAISE EXCEPTION 'checkout_dispatch_isolation_not_ready' USING ERRCODE = '55000';
            END IF;
            """;
        string policyGuard = $"""
            {sourceGuard}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                WHERE policy.polrelid = {Text(effects)}::regclass AND policy.polname = '{PolicyName}'
                    AND policy.polcmd = '*' AND policy.polpermissive
                    AND policy.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                    AND pg_catalog.pg_get_expr(policy.polqual, policy.polrelid) = 'true'
                    AND pg_catalog.pg_get_expr(policy.polwithcheck, policy.polrelid) = 'true')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                    WHERE policy.polrelid = {Text(effects)}::regclass AND policy.polname <> '{PolicyName}'
                        AND EXISTS (SELECT 1 FROM unnest(policy.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}', role_id, 'USAGE'))
                        AND (NOT policy.polpermissive OR policy.polname <> 'tenant_isolation')) THEN
                RAISE EXCEPTION 'checkout_dispatch_policy_not_ready' USING ERRCODE = '55000';
            END IF;
            """;
        int pending = (int)OutboxMessageStatus.Pending;
        int failed = (int)OutboxMessageStatus.Failed;
        int processing = (int)OutboxMessageStatus.Processing;
        return $"""
            DO $contract$
            DECLARE v_role text;
            BEGIN
                {sourceGuard}
                IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {Text(enrollment.GlobalLogin)}
                        AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                        AND NOT rolcreaterole AND NOT rolreplication) THEN
                    RAISE EXCEPTION 'checkout_dispatch_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')) THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                        WHERE namespace.nspname = {Text(Schema(context))}
                            AND routine.proname = {Text(contract.Routines[0].Name)})
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                            JOIN pg_catalog.pg_class relation ON relation.oid = policy.polrelid
                            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                            WHERE namespace.nspname = {Text(Schema(context))} AND policy.polname = '{PolicyName}') THEN
                        RAISE EXCEPTION 'checkout_dispatch_partial_bootstrap' USING ERRCODE = '55000';
                    END IF;
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END LOOP;
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT {GlobalRole} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {Text(enrollment.GlobalLogin)});
                    EXECUTE format('GRANT {MigratorRole} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {Text(enrollment.MigratorLogin)});
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{GlobalRole}'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                            JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                            WHERE parent.rolname = v_role AND member.rolname = {Text(enrollment.MigratorLogin)}) THEN
                            EXECUTE format('REVOKE %I FROM %I', v_role, {Text(enrollment.MigratorLogin)});
                        END IF;
                    END LOOP;
                ELSE
                    {guard}
                    {policyGuard}
                END IF;
            END $contract$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {effects} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT (id, tenant_id, registration_order_id, payment_attempt_id, status, next_attempt_at,
                created_at, processing_lease_token, processing_lease_expires_at, processing_fence, attempt_count,
                unknown_at, last_failure_code, processing_lease_owner, updated_at) ON {effects} TO {OwnerRole};
            GRANT UPDATE (status, next_attempt_at, processing_lease_owner, processing_lease_token,
                processing_lease_expires_at, processing_fence, attempt_count, updated_at) ON {effects} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {effects};
            CREATE POLICY "{PolicyName}" ON {effects} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {claim}(
                    p_token uuid, p_owner text, p_claimed timestamp with time zone, p_expires timestamp with time zone)
                RETURNS TABLE ("EffectId" uuid, "TenantId" uuid, "RegistrationOrderId" uuid, "PaymentAttemptId" uuid,
                    "LeaseToken" uuid, "ProcessingFence" bigint, "ReplayKind" integer, "AttemptCount" integer,
                    "LeaseExpiresAt" timestamp with time zone)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    {policyGuard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR current_setting('role') NOT IN ('none', {Text(enrollment.GlobalLogin)})
                        OR NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                            WHERE (pg_catalog.pg_has_role(session_user, role.oid, 'USAGE')
                                OR pg_catalog.pg_has_role(session_user, role.oid, 'SET'))
                                AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                                    OR role.rolcreaterole OR role.rolreplication)) THEN
                        RAISE EXCEPTION 'checkout_dispatch_global_login_required' USING ERRCODE = '42501';
                    END IF;
                    IF p_token IS NULL OR p_token = '00000000-0000-0000-0000-000000000000'::uuid
                        OR p_owner IS NULL OR p_owner <> btrim(p_owner)
                        OR length(p_owner) NOT BETWEEN 1 AND {CheckoutDispatchEffect.MaxLeaseOwnerLength}
                        OR p_claimed IS NULL OR NOT isfinite(p_claimed)
                        OR p_expires IS NULL OR NOT isfinite(p_expires) OR p_expires <= p_claimed THEN
                        RAISE EXCEPTION 'invalid checkout dispatch claim bounds' USING ERRCODE = '22023';
                    END IF;
                    IF EXISTS (SELECT 1 FROM {effects} effect WHERE effect.processing_lease_token = p_token) THEN
                        RAISE EXCEPTION 'checkout dispatch acquisition already exists' USING ERRCODE = '55000';
                    END IF;
                    RETURN QUERY WITH candidate AS MATERIALIZED (
                        SELECT effect.id, effect.tenant_id,
                            CASE WHEN effect.status = {failed} AND effect.unknown_at IS NOT NULL THEN {(int)CheckoutDispatchReplayKind.UnknownRedrive}
                                WHEN effect.status = {failed} AND effect.last_failure_code IS NOT NULL THEN {(int)CheckoutDispatchReplayKind.PreHandoffRetry}
                                ELSE {(int)CheckoutDispatchReplayKind.None} END AS replay
                        FROM {effects} effect
                        WHERE (effect.status IN ({pending}, {failed})
                                AND (effect.next_attempt_at IS NULL OR effect.next_attempt_at <= p_claimed)
                                AND effect.processing_lease_token IS NULL AND effect.processing_lease_expires_at IS NULL)
                            OR (effect.status = {processing} AND effect.processing_lease_expires_at <= p_claimed)
                        ORDER BY COALESCE(effect.next_attempt_at, effect.created_at), effect.id
                        LIMIT 1 FOR UPDATE OF effect SKIP LOCKED
                    ), updated AS (
                        UPDATE {effects} effect SET status = {processing}, processing_lease_owner = p_owner,
                            processing_lease_token = p_token, processing_lease_expires_at = p_expires,
                            processing_fence = effect.processing_fence + 1, attempt_count = effect.attempt_count + 1,
                            next_attempt_at = NULL, updated_at = p_claimed
                        FROM candidate WHERE effect.id = candidate.id AND effect.tenant_id = candidate.tenant_id
                        RETURNING effect.id, effect.tenant_id, effect.registration_order_id, effect.payment_attempt_id,
                            effect.processing_lease_token, effect.processing_fence, candidate.replay,
                            effect.attempt_count, effect.processing_lease_expires_at
                    ) SELECT * FROM updated;
                END $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $contract$
            BEGIN
                {guard}
                {policyGuard}
                IF pg_catalog.has_schema_privilege('{OwnerRole}', {Text(Schema(context))}, 'CREATE') THEN
                    RAISE EXCEPTION 'checkout owner retains effective schema creation authority' USING ERRCODE = '42501';
                END IF;
            END $contract$;
            """;
    }
}
