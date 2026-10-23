using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Independent execute-only subscription admission, not an entitlement on another queue.</summary>
public static class PostgresRegistrationProviderSubscriptionClaimContract
{
    public const string OwnerRole = "event_subscription_claim_owner";
    public const string MigratorRole = "event_subscription_claim_migrator";
    public const string GlobalRole = "event_subscription_claim_global";
    private const string Routine = "registration_provider_subscription_claim";
    private const string Arguments = "integer, integer, timestamp with time zone, interval";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Routine, Schema(context));

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        enrollment.Validate();
        var entity = context.Model.FindEntityType(typeof(RegistrationProviderSubscriptionState))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string Column(string property) => entity.FindProperty(property)!.GetColumnName(store)!;
        string C(string property) => sql.DelimitIdentifier(Column(property));
        string schema = sql.DelimitIdentifier(Schema(context));
        string table = sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
        string function = FunctionSql(context);
        string[] reads =
        [
            nameof(RegistrationProviderSubscriptionState.Id), nameof(RegistrationProviderSubscriptionState.TenantId),
            nameof(RegistrationProviderSubscriptionState.RegistrationProviderBindingId),
            nameof(RegistrationProviderSubscriptionState.ProcessingGeneration), nameof(RegistrationProviderSubscriptionState.LeaseExpiresAt),
            nameof(RegistrationProviderSubscriptionState.IsDeleted), nameof(RegistrationProviderSubscriptionState.WatchExpiresAt),
            nameof(RegistrationProviderSubscriptionState.FailureCategory), nameof(RegistrationProviderSubscriptionState.NextRenewalAttemptAt),
            nameof(RegistrationProviderSubscriptionState.PendingNotificationAt), nameof(RegistrationProviderSubscriptionState.NextSweepAttemptAt)
        ];
        string[] writes =
        [
            nameof(RegistrationProviderSubscriptionState.LeaseToken), nameof(RegistrationProviderSubscriptionState.LeaseExpiresAt),
            nameof(RegistrationProviderSubscriptionState.ProcessingGeneration), nameof(RegistrationProviderSubscriptionState.ConcurrencyStamp),
            nameof(RegistrationProviderSubscriptionState.UpdatedAt)
        ];
        var contract = new PostgresPrivilegeGuard.Contract(Schema(context), [OwnerRole, MigratorRole, GlobalRole],
            [new(Routine, Arguments, OwnerRole, [GlobalRole])],
            [new(entity.GetTableName()!, OwnerRole, [], reads.Concat(writes).Distinct(StringComparer.Ordinal)
                .Select(property => new PostgresPrivilegeGuard.Column(Column(property),
                    (reads.Contains(property) ? new[] { "SELECT" } : [])
                    .Concat(writes.Contains(property) ? ["UPDATE"] : []).ToArray())).ToArray())],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string assertion = PostgresPrivilegeGuard.BuildAssertSql(contract);
        string id = C(nameof(RegistrationProviderSubscriptionState.Id));
        string tenant = C(nameof(RegistrationProviderSubscriptionState.TenantId));
        string binding = C(nameof(RegistrationProviderSubscriptionState.RegistrationProviderBindingId));
        string generation = C(nameof(RegistrationProviderSubscriptionState.ProcessingGeneration));
        string token = C(nameof(RegistrationProviderSubscriptionState.LeaseToken));
        string expires = C(nameof(RegistrationProviderSubscriptionState.LeaseExpiresAt));
        string watch = C(nameof(RegistrationProviderSubscriptionState.WatchExpiresAt));
        string failure = C(nameof(RegistrationProviderSubscriptionState.FailureCategory));
        string renewal = C(nameof(RegistrationProviderSubscriptionState.NextRenewalAttemptAt));
        string notification = C(nameof(RegistrationProviderSubscriptionState.PendingNotificationAt));
        string sweep = C(nameof(RegistrationProviderSubscriptionState.NextSweepAttemptAt));
        string deleted = C(nameof(RegistrationProviderSubscriptionState.IsDeleted));
        string stamp = C(nameof(RegistrationProviderSubscriptionState.ConcurrencyStamp));
        string updated = C(nameof(RegistrationProviderSubscriptionState.UpdatedAt));
        string lockKey = RelationalNamedLock.ComputeStableKey("registration-provider-subscription-state-claim")
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"""
            DO $enrollment$
            DECLARE v_role text;
            BEGIN
                IF session_user IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR EXISTS (SELECT 1 FROM unnest(ARRAY[{Text(enrollment.MigratorLogin)}, {Text(enrollment.GlobalLogin)},
                        {Text(enrollment.RuntimeLogin)}, {Text(enrollment.EmailInstanceMutationLogin)},
                        {Text(enrollment.EmailTenantMutationLogin)}]) login(name)
                        LEFT JOIN pg_catalog.pg_roles role ON role.rolname = login.name
                        WHERE role.oid IS NULL OR NOT role.rolcanlogin
                            OR login.name IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'native_queue_enrollment_not_ready' USING ERRCODE = '42501';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')) THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {Text(contract.Schema)} AND p.proname = '{Routine}')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p JOIN pg_catalog.pg_class c ON c.oid = p.polrelid
                            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                            WHERE n.nspname = {Text(contract.Schema)} AND p.polname = 'subscription_claim_queue') THEN
                        RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                    END IF;
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END LOOP;
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT %I TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', '{GlobalRole}', {Text(enrollment.GlobalLogin)});
                    EXECUTE format('GRANT %I TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', '{MigratorRole}', {Text(enrollment.MigratorLogin)});
                    -- Keep creator ADMIN until the declared grants exist. Only fresh
                    -- owner/executor shortcuts are removed, never the declared migrator edge.
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{GlobalRole}'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members membership
                            JOIN pg_catalog.pg_roles parent ON parent.oid = membership.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = membership.member
                            WHERE parent.rolname = v_role AND member.rolname = {Text(enrollment.MigratorLogin)}) THEN
                            EXECUTE format('REVOKE %I FROM %I', v_role, {Text(enrollment.MigratorLogin)});
                        END IF;
                    END LOOP;
                    IF ({PostgresPrivilegeGuard.BuildRoleGraphSql(contract)}) IS NOT TRUE THEN
                        RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                    END IF;
                ELSE
                    {assertion}
                END IF;
            END $enrollment$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({string.Join(", ", reads.Select(C))}) ON {table} TO {OwnerRole};
            GRANT UPDATE ({string.Join(", ", writes.Select(C))}) ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS subscription_claim_queue ON {table};
            CREATE POLICY subscription_claim_queue ON {table} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DO $bootstrap$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(
                    p_operation integer, p_take integer, p_observed timestamp with time zone, p_duration interval)
                RETURNS TABLE (state_id uuid, tenant_id uuid, registration_provider_binding_id uuid,
                    lease_token uuid, processing_generation bigint, lease_expires_at timestamp with time zone)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE v_at timestamp with time zone; q record; minted uuid;
                BEGIN
                    {assertion}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    IF session_user IS DISTINCT FROM {Text(enrollment.GlobalLogin)} THEN
                        RAISE EXCEPTION 'subscription_claim_session_not_enrolled' USING ERRCODE = '42501';
                    END IF;
                    IF p_operation IS NULL OR p_operation NOT IN (1, 2) OR p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000
                        OR p_observed IS NULL OR NOT isfinite(p_observed)
                        OR p_duration IS NULL OR NOT isfinite(p_duration)
                        OR p_duration <= interval '0' OR p_duration > interval '2 minutes' THEN
                        RAISE EXCEPTION 'invalid subscription claim bounds' USING ERRCODE = '22023';
                    END IF;
                    v_at := LEAST(p_observed, statement_timestamp());
                    PERFORM pg_advisory_xact_lock({lockKey});
                    FOR q IN SELECT queue.{id} AS id, queue.{tenant} AS tenant, queue.{binding} AS binding,
                            queue.{generation} AS generation
                        FROM {table} queue WHERE NOT queue.{deleted}
                            AND (queue.{expires} IS NULL OR queue.{expires} <= v_at)
                            AND ((p_operation = 1 AND queue.{watch} <= v_at + interval '2 days'
                                AND (queue.{failure} IS NULL OR queue.{failure} NOT IN ('renewal_in_doubt', 'renewal_rejected'))
                                AND (queue.{renewal} IS NULL OR queue.{renewal} <= v_at))
                                OR (p_operation = 2 AND ((queue.{notification} IS NOT NULL
                                    AND (queue.{sweep} IS NULL OR queue.{sweep} <= v_at))
                                    OR (queue.{notification} IS NULL AND queue.{sweep} <= v_at))))
                        ORDER BY CASE WHEN p_operation = 1 THEN queue.{watch}
                            ELSE coalesce(queue.{notification}, queue.{sweep}) END, queue.{id}
                        LIMIT p_take FOR UPDATE OF queue SKIP LOCKED
                    LOOP
                        minted := uuidv7();
                        UPDATE {table} queue SET {token} = minted, {expires} = v_at + p_duration,
                            {generation} = q.generation + 1, {stamp} = uuidv7(), {updated} = v_at
                            WHERE queue.{id} = q.id AND queue.{tenant} = q.tenant AND queue.{binding} = q.binding;
                        state_id := q.id; tenant_id := q.tenant; registration_provider_binding_id := q.binding;
                        lease_token := minted; processing_generation := q.generation + 1; lease_expires_at := v_at + p_duration;
                        RETURN NEXT;
                    END LOOP;
                END $function$ $ddl$;
                EXECUTE $acl$ REVOKE ALL ON FUNCTION {function}({Arguments}) FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {function}({Arguments}) TO {GlobalRole}; $acl$;
                PERFORM set_config('role', previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $ready$ BEGIN {assertion} END $ready$;
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(RegistrationProviderSubscriptionState))!.GetSchema() ?? "public";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
