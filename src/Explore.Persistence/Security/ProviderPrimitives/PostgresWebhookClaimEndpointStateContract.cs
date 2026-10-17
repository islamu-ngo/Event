using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Original-claim Runtime authority for retained endpoint status and health, never endpoint configuration.
/// Health executes before the target releases its lease, in the caller's settlement transaction.
/// </summary>
public static class PostgresWebhookClaimEndpointStateContract
{
    public const string OwnerRole = "event_webhook_claim_endpoint_owner";
    public const string MigratorRole = "event_webhook_claim_endpoint_migrator";
    public const string RuntimeRole = "event_webhook_claim_endpoint_runtime";
    private const string Routine = "webhook_claim_endpoint_state";
    private const string Policy = "webhook_claim_endpoint_state";
    private const string Arguments = "uuid, uuid, uuid, bigint, uuid, timestamp with time zone, integer, text, integer";

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

    internal static async Task<WebhookEndpointFailureState?> InvokeAsync(
        ExploreDbContext context, Guid tenantId, Guid targetId, Guid leaseToken, long fence,
        Guid? endpointId, DateTimeOffset observedAt, int operation, string? category, int threshold,
        CancellationToken cancellationToken)
    {
        if (context.IsTenantFilterBypassed || context.BindTenantLease() != tenantId || tenantId == Guid.Empty)
            throw new InvalidOperationException("Webhook endpoint state requires selected-tenant Runtime authority.");
        if (operation != 0 && context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Webhook endpoint health requires the settlement transaction.");
        return await context.Database.SqlQueryRaw<WebhookEndpointFailureState>(
                $"SELECT * FROM {FunctionSql(context)}(@tenant, @target, @lease, @fence, @endpoint, @observed, @operation, @category, @threshold)",
                new NpgsqlParameter("tenant", tenantId), new NpgsqlParameter("target", targetId),
                new NpgsqlParameter("lease", leaseToken), new NpgsqlParameter("fence", fence),
                new NpgsqlParameter("endpoint", NpgsqlDbType.Uuid) { Value = (object?)endpointId ?? DBNull.Value },
                new NpgsqlParameter("observed", observedAt), new NpgsqlParameter("operation", operation),
                new NpgsqlParameter("category", NpgsqlDbType.Text) { Value = (object?)category ?? DBNull.Value },
                new NpgsqlParameter("threshold", threshold))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        enrollment.Validate();
        var sql = context.GetService<ISqlGenerationHelper>();
        string C<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string T(string property) => C<WebhookLocalTargetSnapshot>(property);
        string E(string property) => C<WebhookEndpoint>(property);
        string P(string property) => C<WebhookDeliveryPlanSnapshot>(property);
        string M(string property) => C<WebhookMessage>(property);
        string Cn(string property) => C<WebhookConsumer>(property);

        PostgresPrivilegeGuard.Relation Relation<T>(string[] reads, string[] writes)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return new(entity.GetTableName()!, OwnerRole, [], reads.Concat(writes).Distinct(StringComparer.Ordinal)
                .Select(property => new PostgresPrivilegeGuard.Column(entity.FindProperty(property)!.GetColumnName(store)!,
                    (reads.Contains(property) ? new[] { "SELECT" } : [])
                    .Concat(writes.Contains(property) ? ["UPDATE"] : []).ToArray())).ToArray());
        }

        var relations = new[]
        {
            Relation<WebhookLocalTargetSnapshot>(
                [nameof(WebhookLocalTargetSnapshot.Id), nameof(WebhookLocalTargetSnapshot.TenantId),
                 nameof(WebhookLocalTargetSnapshot.WebhookEndpointId), nameof(WebhookLocalTargetSnapshot.WebhookMessageId),
                 nameof(WebhookLocalTargetSnapshot.DeliveryPlanSnapshotId), nameof(WebhookLocalTargetSnapshot.DeliveryStatusId),
                 nameof(WebhookLocalTargetSnapshot.ProcessingLeaseToken), nameof(WebhookLocalTargetSnapshot.DeliveryFence),
                 nameof(WebhookLocalTargetSnapshot.ProcessingLeaseExpiresAtUtc)],
                // PostgreSQL requires UPDATE privilege for a row lock; the function never writes this column.
                [nameof(WebhookLocalTargetSnapshot.ConcurrencyVersion)]),
            Relation<WebhookDeliveryPlanSnapshot>(
                [nameof(WebhookDeliveryPlanSnapshot.Id), nameof(WebhookDeliveryPlanSnapshot.TenantId),
                 nameof(WebhookDeliveryPlanSnapshot.WebhookMessageId), nameof(WebhookDeliveryPlanSnapshot.WebhookConsumerId),
                 nameof(WebhookDeliveryPlanSnapshot.ProviderModeId), nameof(WebhookDeliveryPlanSnapshot.AttemptRetentionUntilUtc)], []),
            Relation<WebhookMessage>([nameof(WebhookMessage.Id), nameof(WebhookMessage.TenantId)], []),
            Relation<WebhookConsumer>(
                [nameof(WebhookConsumer.Id), nameof(WebhookConsumer.TenantId), nameof(WebhookConsumer.InstanceId),
                 nameof(WebhookConsumer.ConsumerKindId)], []),
            Relation<WebhookEndpoint>(
                [nameof(WebhookEndpoint.Id), nameof(WebhookEndpoint.TenantId), nameof(WebhookEndpoint.InstanceId),
                 nameof(WebhookEndpoint.ConsumerId), nameof(WebhookEndpoint.StatusId),
                 nameof(WebhookEndpoint.ConsecutiveFailureCount), nameof(WebhookEndpoint.DeliveryStateVersion)],
                [nameof(WebhookEndpoint.LastSuccessAt), nameof(WebhookEndpoint.LastFailureAt),
                 nameof(WebhookEndpoint.ConsecutiveFailureCount), nameof(WebhookEndpoint.DeliveryStateVersion),
                 nameof(WebhookEndpoint.StatusId), nameof(WebhookEndpoint.CircuitOpenedAt),
                 nameof(WebhookEndpoint.AutoPausedAt), nameof(WebhookEndpoint.AutoPauseReason), nameof(WebhookEndpoint.UpdatedAt)])
        };
        var contract = new PostgresPrivilegeGuard.Contract(Schema(context), [OwnerRole, MigratorRole, RuntimeRole],
            [new(Routine, Arguments, OwnerRole, [RuntimeRole])], relations,
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)
            ]);
        string assertion = PostgresPrivilegeGuard.BuildAssertSql(contract);
        string schema = sql.DelimitIdentifier(Schema(context));
        string grants = string.Join(Environment.NewLine, relations.Select(relation =>
        {
            string table = sql.DelimitIdentifier(relation.Name, Schema(context));
            string privileges = string.Join(Environment.NewLine, new[] { "SELECT", "UPDATE" }
                .Where(privilege => relation.Columns.Any(column => column.Privileges.Contains(privilege)))
                .Select(privilege => $"GRANT {privilege} ({string.Join(", ", relation.Columns.Where(column => column.Privileges.Contains(privilege)).Select(column => sql.DelimitIdentifier(column.Name)))}) ON {table} TO {OwnerRole};"));
            return $"""
                REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {RuntimeRole};
                {privileges}
                DROP POLICY IF EXISTS {Policy} ON {table};
                CREATE POLICY {Policy} ON {table} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
                """;
        }));
        string function = FunctionSql(context);
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
                            OR login.name IN ('{OwnerRole}', '{MigratorRole}', '{RuntimeRole}')) THEN
                    RAISE EXCEPTION 'native_queue_enrollment_not_ready' USING ERRCODE = '42501';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{RuntimeRole}')) THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {Text(contract.Schema)} AND p.proname = '{Routine}')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p JOIN pg_catalog.pg_class c ON c.oid = p.polrelid
                            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                            WHERE n.nspname = {Text(contract.Schema)} AND p.polname = '{Policy}') THEN
                        RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                    END IF;
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{RuntimeRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END LOOP;
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT %I TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', '{RuntimeRole}', {Text(enrollment.RuntimeLogin)});
                    EXECUTE format('GRANT %I TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', '{MigratorRole}', {Text(enrollment.MigratorLogin)});
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{RuntimeRole}'] LOOP
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
            GRANT USAGE ON SCHEMA {schema} TO {RuntimeRole};
            {grants}
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(
                    p_tenant uuid, p_target uuid, p_token uuid, p_fence bigint, p_endpoint uuid,
                    p_observed timestamp with time zone, p_operation integer, p_category text, p_threshold integer)
                RETURNS TABLE ("ConsecutiveFailureCount" integer, "IsAutoPaused" boolean, "TransitionedToAutoPaused" boolean)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE endpoint_id uuid; state_version bigint; failure_count integer;
                    lease_end timestamp with time zone; retention_end timestamp with time zone; v_at timestamp with time zone;
                BEGIN
                    {assertion}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    IF session_user IS DISTINCT FROM {Text(enrollment.RuntimeLogin)}
                        OR current_setting('role') NOT IN ('none', {Text(enrollment.RuntimeLogin)})
                        OR p_tenant IS NULL OR p_tenant = '{Guid.Empty}'::uuid
                        OR nullif(current_setting('app.current_tenant_id', true), '')::uuid IS DISTINCT FROM p_tenant THEN
                        RAISE EXCEPTION 'webhook_endpoint_runtime_tenant_required' USING ERRCODE = '42501';
                    END IF;
                    IF p_target IS NULL OR p_target = '{Guid.Empty}'::uuid
                        OR p_token IS NULL OR p_token = '{Guid.Empty}'::uuid OR p_fence IS NULL OR p_fence < 1
                        OR p_observed IS NULL OR NOT isfinite(p_observed)
                        OR p_operation IS NULL OR p_operation NOT IN (0, 1, 2)
                        OR (p_operation <> 0 AND (p_endpoint IS NULL OR p_endpoint = '{Guid.Empty}'::uuid))
                        OR (p_operation = 2 AND (p_threshold IS NULL OR p_threshold < 1
                            OR p_category IS NULL OR length(p_category) NOT BETWEEN 1 AND 100)) THEN
                        RAISE EXCEPTION 'webhook_endpoint_claim_invalid' USING ERRCODE = '22023';
                    END IF;
                    v_at := greatest(p_observed, clock_timestamp());
                    SELECT e.{E(nameof(WebhookEndpoint.Id))}, e.{E(nameof(WebhookEndpoint.DeliveryStateVersion))},
                        e.{E(nameof(WebhookEndpoint.ConsecutiveFailureCount))},
                        t.{T(nameof(WebhookLocalTargetSnapshot.ProcessingLeaseExpiresAtUtc))},
                        p.{P(nameof(WebhookDeliveryPlanSnapshot.AttemptRetentionUntilUtc))}
                    INTO endpoint_id, state_version, failure_count, lease_end, retention_end
                    FROM {Table<WebhookLocalTargetSnapshot>()} t
                    JOIN {Table<WebhookDeliveryPlanSnapshot>()} p
                        ON p.{P(nameof(WebhookDeliveryPlanSnapshot.Id))} = t.{T(nameof(WebhookLocalTargetSnapshot.DeliveryPlanSnapshotId))}
                        AND p.{P(nameof(WebhookDeliveryPlanSnapshot.TenantId))} = p_tenant
                        AND p.{P(nameof(WebhookDeliveryPlanSnapshot.WebhookMessageId))} = t.{T(nameof(WebhookLocalTargetSnapshot.WebhookMessageId))}
                    JOIN {Table<WebhookMessage>()} m
                        ON m.{M(nameof(WebhookMessage.Id))} = t.{T(nameof(WebhookLocalTargetSnapshot.WebhookMessageId))}
                        AND m.{M(nameof(WebhookMessage.TenantId))} = p_tenant
                    JOIN {Table<WebhookEndpoint>()} e
                        ON e.{E(nameof(WebhookEndpoint.Id))} = t.{T(nameof(WebhookLocalTargetSnapshot.WebhookEndpointId))}
                        AND e.{E(nameof(WebhookEndpoint.ConsumerId))} = p.{P(nameof(WebhookDeliveryPlanSnapshot.WebhookConsumerId))}
                    JOIN {Table<WebhookConsumer>()} c ON c.{Cn(nameof(WebhookConsumer.Id))} = e.{E(nameof(WebhookEndpoint.ConsumerId))}
                    WHERE t.{T(nameof(WebhookLocalTargetSnapshot.Id))} = p_target
                        AND t.{T(nameof(WebhookLocalTargetSnapshot.TenantId))} = p_tenant
                        AND t.{T(nameof(WebhookLocalTargetSnapshot.ProcessingLeaseToken))} = p_token
                        AND t.{T(nameof(WebhookLocalTargetSnapshot.DeliveryFence))} = p_fence
                        AND t.{T(nameof(WebhookLocalTargetSnapshot.DeliveryStatusId))} = {(int)WebhookLocalDeliveryStatus.Delivering}
                        AND t.{T(nameof(WebhookLocalTargetSnapshot.ProcessingLeaseExpiresAtUtc))} > v_at
                        AND p.{P(nameof(WebhookDeliveryPlanSnapshot.AttemptRetentionUntilUtc))} > v_at
                        AND p.{P(nameof(WebhookDeliveryPlanSnapshot.ProviderModeId))} IN ({(int)WebhookProviderMode.Local}, {(int)WebhookProviderMode.Composite})
                        AND (p_endpoint IS NULL OR e.{E(nameof(WebhookEndpoint.Id))} = p_endpoint)
                        AND e.{E(nameof(WebhookEndpoint.StatusId))} = {(int)WebhookEndpointStatus.Active}
                        AND ((e.{E(nameof(WebhookEndpoint.TenantId))} = p_tenant AND c.{Cn(nameof(WebhookConsumer.TenantId))} = p_tenant)
                            OR (e.{E(nameof(WebhookEndpoint.TenantId))} IS NULL AND e.{E(nameof(WebhookEndpoint.InstanceId))} IS NOT NULL
                                AND c.{Cn(nameof(WebhookConsumer.TenantId))} IS NULL
                                AND c.{Cn(nameof(WebhookConsumer.InstanceId))} = e.{E(nameof(WebhookEndpoint.InstanceId))}
                                AND c.{Cn(nameof(WebhookConsumer.ConsumerKindId))} = {(int)WebhookConsumerKind.Instance}))
                    FOR UPDATE OF t, e;
                    IF NOT FOUND OR lease_end <= greatest(p_observed, clock_timestamp())
                        OR retention_end <= greatest(p_observed, clock_timestamp()) THEN RETURN; END IF;
                    IF p_operation = 1 THEN
                        UPDATE {Table<WebhookEndpoint>()} SET
                            {E(nameof(WebhookEndpoint.LastSuccessAt))} = p_observed,
                            {E(nameof(WebhookEndpoint.ConsecutiveFailureCount))} = 0,
                            {E(nameof(WebhookEndpoint.DeliveryStateVersion))} = state_version + 1,
                            {E(nameof(WebhookEndpoint.UpdatedAt))} = p_observed
                        WHERE {E(nameof(WebhookEndpoint.Id))} = endpoint_id
                            AND {E(nameof(WebhookEndpoint.DeliveryStateVersion))} = state_version
                            AND {E(nameof(WebhookEndpoint.StatusId))} = {(int)WebhookEndpointStatus.Active};
                        IF NOT FOUND THEN RETURN; END IF;
                        failure_count := 0;
                    ELSIF p_operation = 2 THEN
                        failure_count := failure_count + 1;
                        UPDATE {Table<WebhookEndpoint>()} SET
                            {E(nameof(WebhookEndpoint.LastFailureAt))} = p_observed,
                            {E(nameof(WebhookEndpoint.ConsecutiveFailureCount))} = failure_count,
                            {E(nameof(WebhookEndpoint.DeliveryStateVersion))} = state_version + 1,
                            {E(nameof(WebhookEndpoint.UpdatedAt))} = p_observed
                        WHERE {E(nameof(WebhookEndpoint.Id))} = endpoint_id
                            AND {E(nameof(WebhookEndpoint.DeliveryStateVersion))} = state_version
                            AND {E(nameof(WebhookEndpoint.StatusId))} = {(int)WebhookEndpointStatus.Active};
                        IF NOT FOUND THEN RETURN; END IF;
                        IF failure_count >= p_threshold THEN
                            UPDATE {Table<WebhookEndpoint>()} SET
                                {E(nameof(WebhookEndpoint.StatusId))} = {(int)WebhookEndpointStatus.AutoPaused},
                                {E(nameof(WebhookEndpoint.CircuitOpenedAt))} = p_observed,
                                {E(nameof(WebhookEndpoint.AutoPausedAt))} = p_observed,
                                {E(nameof(WebhookEndpoint.AutoPauseReason))} = p_category
                            WHERE {E(nameof(WebhookEndpoint.Id))} = endpoint_id;
                        END IF;
                    END IF;
                    RETURN QUERY SELECT failure_count, p_operation = 2 AND failure_count >= p_threshold,
                        p_operation = 2 AND failure_count >= p_threshold;
                END $body$ $ddl$;
                EXECUTE $acl$ REVOKE ALL ON FUNCTION {function}({Arguments}) FROM PUBLIC, {RuntimeRole};
                    GRANT EXECUTE ON FUNCTION {function}({Arguments}) TO {RuntimeRole}; $acl$;
                PERFORM set_config('role', previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $ready$ BEGIN {assertion} END $ready$;
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!.GetSchema() ?? "public";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
