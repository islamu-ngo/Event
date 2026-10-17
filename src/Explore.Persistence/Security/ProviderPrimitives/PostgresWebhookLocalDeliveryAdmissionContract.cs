using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings.Groups;
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
/// Execute-only Global discovery, governance, fair claims and expired-lease recovery.
/// The definer owns ledger-column authority; payload, destination and signing references remain tenant-private.
/// </summary>
public static partial class PostgresWebhookLocalDeliveryAdmissionContract
{
    public const string OwnerRole = "event_local_webhook_admission_owner";
    public const string MigratorRole = "event_local_webhook_admission_migrator";
    public const string GlobalRole = "event_local_webhook_admission_global";
    public const string PolicyName = "local_webhook_delivery_admission";

    public static string DiscoveryFunctionSql(ExploreDbContext context) => FunctionSql(context, "_due_tenants");
    public static string ClaimFunctionSql(ExploreDbContext context) => FunctionSql(context, "_claim");
    public static string RecoveryFunctionSql(ExploreDbContext context) => FunctionSql(context, "_recover");
    public static string CountFunctionSql(ExploreDbContext context) => FunctionSql(context, "_counts");
    public static string SystemGovernanceFunctionSql(ExploreDbContext context) => FunctionSql(context, "_system_governance");
    public static string TenantGovernanceFunctionSql(ExploreDbContext context) => FunctionSql(context, "_tenant_governance");

    private static string FunctionSql(ExploreDbContext context, string suffix)
    {
        var entity = context.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + suffix, Schema(context, entity));
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<Guid>> DiscoverAsync(
        ExploreDbContext context, int tenantLimit, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<Guid>(
            $"SELECT * FROM {DiscoveryFunctionSql(context)}({{0}}, {{1}})", nowUtc, tenantLimit)
            .ToListAsync(cancellationToken);

    internal static async Task<int> CountAsync(
        ExploreDbContext context, bool stale, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<int>(
            $"SELECT {CountFunctionSql(context)}({{0}}, {{1}}) AS \"Value\"", stale, nowUtc)
            .SingleAsync(cancellationToken);

    internal static async Task<IReadOnlyList<WebhookLocalDeliveryClaim>> ClaimAsync(
        ExploreDbContext context, WebhookLocalTargetClaimRequest request,
        IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits> limits, CancellationToken cancellationToken)
    {
        Guid[] tenants = request.TenantOrder.Where(limits.ContainsKey).Distinct().ToArray();
        if (tenants.Length == 0 || request.BatchSize is < 1 or > 1000 ||
            request.CandidateBatchSize < request.BatchSize || request.GlobalInFlightLimit < 1 ||
            request.LeaseDuration <= TimeSpan.Zero)
            return [];
        return await context.Database.SqlQueryRaw<WebhookLocalDeliveryClaim>(
            $"SELECT * FROM {ClaimFunctionSql(context)}(@now, @duration, @batch, @candidates, @global, @tenants, @tenant_caps, @endpoint_caps, @cycles, @target)",
            new NpgsqlParameter("now", request.ClaimedAtUtc),
            new NpgsqlParameter("duration", request.LeaseDuration),
            new NpgsqlParameter("batch", request.BatchSize),
            new NpgsqlParameter("candidates", request.CandidateBatchSize),
            new NpgsqlParameter("global", request.GlobalInFlightLimit),
            new NpgsqlParameter("tenants", tenants),
            new NpgsqlParameter("tenant_caps", tenants.Select(id => limits[id].MaxInFlightPerTenant).ToArray()),
            new NpgsqlParameter("endpoint_caps", tenants.Select(id => limits[id].MaxInFlightPerEndpoint).ToArray()),
            new NpgsqlParameter("cycles", tenants.Select(id => limits[id].MaxItemsPerClaimCycle).ToArray()),
            new NpgsqlParameter("target", NpgsqlDbType.Uuid) { Value = (object?)request.TargetId ?? DBNull.Value })
            .ToListAsync(cancellationToken);
    }

    internal static async Task<int> RecoverAsync(
        ExploreDbContext context, DateTimeOffset recoveredAtUtc, string failureCategory, int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 1000)
            return 0;
        return await context.Database.SqlQueryRaw<int>(
            $"SELECT {RecoveryFunctionSql(context)}({{0}}, {{1}}, {{2}}) AS \"Value\"",
            recoveredAtUtc, failureCategory, batchSize).SingleAsync(cancellationToken);
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
                RAISE EXCEPTION 'local_webhook_session_not_enrolled' USING ERRCODE = '42501';
            END IF;
            """;
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table<T>()
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.GetTableName()!, Schema(context, entity));
        }
        string Column<T>(string name)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(mapping)!);
        }
        string Columns<T>(params string[] names) => string.Join(", ", names.Select(Column<T>));
        string AllColumns<T>() => string.Join(", ", context.Model.FindEntityType(typeof(T))!.GetProperties()
            .Select(property => Column<T>(property.Name)));
        string schema = sql.DelimitIdentifier(Schema(context, context.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!));
        string targets = Table<WebhookLocalTargetSnapshot>();
        string endpoints = Table<WebhookEndpoint>();
        string attempts = Table<WebhookDeliveryAttempt>();
        string system = Table<SystemSetting>();
        string tenantSettings = Table<TenantSetting>();
        string id = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.Id));
        string tenant = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.TenantId));
        string endpoint = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.WebhookEndpointId));
        string message = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.WebhookMessageId));
        string status = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.DeliveryStatusId));
        string next = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.NextActionAtUtc));
        string captured = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.CapturedAtUtc));
        string owner = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.ProcessingLeaseOwner));
        string lease = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.ProcessingLeaseToken));
        string expires = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.ProcessingLeaseExpiresAtUtc));
        string fence = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.DeliveryFence));
        string version = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.ConcurrencyVersion));
        string maxAttempts = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.MaxAttempts));
        string created = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.CreatedAt));
        string updated = Column<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.UpdatedAt));
        string endpointId = Column<WebhookEndpoint>(nameof(WebhookEndpoint.Id));
        string endpointStatus = Column<WebhookEndpoint>(nameof(WebhookEndpoint.StatusId));
        string key = Column<TenantSetting>(nameof(TenantSetting.SettingKey));
        string settingsTenant = Column<TenantSetting>(nameof(TenantSetting.TenantId));
        string systemKey = Column<SystemSetting>(nameof(SystemSetting.SettingKey));
        string keys = string.Join(", ", WebhookDeliverySettingGroup.SettingKeys.Select(value => $"'{value}'"));
        string ledgerReads = string.Join(", ", new[] { id, tenant, endpoint, message, status, next, captured,
            lease, expires, fence, version, maxAttempts, created, updated });
        string ledgerWrites = string.Join(", ", new[] { status, owner, lease, expires, fence, version, next, updated });
        string evidenceColumns = Columns<WebhookDeliveryAttempt>(
            nameof(WebhookDeliveryAttempt.Id), nameof(WebhookDeliveryAttempt.TenantId),
            nameof(WebhookDeliveryAttempt.MessageId), nameof(WebhookDeliveryAttempt.EndpointId),
            nameof(WebhookDeliveryAttempt.AttemptNumber), nameof(WebhookDeliveryAttempt.OutcomeId),
            nameof(WebhookDeliveryAttempt.ScheduledAt), nameof(WebhookDeliveryAttempt.SentAt),
            nameof(WebhookDeliveryAttempt.CompletedAt), nameof(WebhookDeliveryAttempt.ProcessingFence),
            nameof(WebhookDeliveryAttempt.FailureCategory), nameof(WebhookDeliveryAttempt.CreatedAt));
        string discovery = DiscoveryFunctionSql(context);
        string claim = ClaimFunctionSql(context);
        string recovery = RecoveryFunctionSql(context);
        string counts = CountFunctionSql(context);
        string systemGovernance = SystemGovernanceFunctionSql(context);
        string tenantGovernance = TenantGovernanceFunctionSql(context);
        string signatures = $"{discovery}(timestamp with time zone, integer), " +
            $"{claim}(timestamp with time zone, interval, integer, integer, integer, uuid[], integer[], integer[], integer[], uuid), " +
            $"{recovery}(timestamp with time zone, text, integer), {counts}(boolean, timestamp with time zone), " +
            $"{systemGovernance}(), {tenantGovernance}(uuid)";
        long lockKey = RelationalNamedLock.ComputeStableKey("webhook-local-target-claim");
        int pending = (int)WebhookLocalDeliveryStatus.Pending;
        int retry = (int)WebhookLocalDeliveryStatus.RetryDue;
        int delivering = (int)WebhookLocalDeliveryStatus.Delivering;
        int deadLettered = (int)WebhookLocalDeliveryStatus.DeadLettered;
        int active = (int)WebhookEndpointStatus.Active;
        return $"""
            {EnrollmentSql(contract, enrollment)}
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {targets}, {endpoints}, {attempts}, {tenantSettings}, {system} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({ledgerReads}), UPDATE ({ledgerWrites}) ON {targets} TO {OwnerRole};
            GRANT SELECT ({endpointId}, {endpointStatus}) ON {endpoints} TO {OwnerRole};
            GRANT INSERT ({evidenceColumns}) ON {attempts} TO {OwnerRole};
            GRANT SELECT ({AllColumns<SystemSetting>()}) ON {system} TO {OwnerRole};
            GRANT SELECT ({AllColumns<TenantSetting>()}) ON {tenantSettings} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {targets};
            CREATE POLICY "{PolicyName}" ON {targets} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {endpoints};
            CREATE POLICY "{PolicyName}" ON {endpoints} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {attempts};
            CREATE POLICY "{PolicyName}" ON {attempts} FOR INSERT TO {OwnerRole} WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {tenantSettings};
            CREATE POLICY "{PolicyName}" ON {tenantSettings} FOR SELECT TO {OwnerRole} USING ({key} IN ({keys}));
            DO $install$
            DECLARE v_previous text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {discovery}(p_now timestamp with time zone, p_take integer)
                RETURNS SETOF uuid LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {invocation}
                    IF p_now IS NULL OR NOT isfinite(p_now) OR p_take IS NULL OR p_take NOT BETWEEN 1 AND 10000 THEN
                        RAISE EXCEPTION 'invalid local webhook discovery bounds' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY SELECT t.{tenant} FROM {targets} t JOIN {endpoints} e ON e.{endpointId} = t.{endpoint}
                        WHERE t.{status} IN ({pending}, {retry}) AND t.{next} <= p_now AND e.{endpointStatus} = {active}
                        GROUP BY t.{tenant} ORDER BY min(t.{next}), t.{tenant} LIMIT p_take;
                END $body$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {counts}(p_stale boolean, p_now timestamp with time zone)
                RETURNS integer LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_count integer;
                BEGIN
                    {invocation}
                    IF p_now IS NULL OR NOT isfinite(p_now) OR p_stale IS NULL THEN
                        RAISE EXCEPTION 'invalid local webhook count bounds' USING ERRCODE = '22023';
                    END IF;
                    IF p_stale THEN
                        SELECT count(*)::integer INTO v_count FROM {targets} t
                            WHERE t.{status} = {delivering} AND t.{expires} <= p_now;
                    ELSE
                        SELECT count(*)::integer INTO v_count FROM {targets} t JOIN {endpoints} e ON e.{endpointId} = t.{endpoint}
                            WHERE t.{status} IN ({pending}, {retry}) AND t.{next} <= p_now AND e.{endpointStatus} = {active};
                    END IF;
                    RETURN v_count;
                END $body$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {claim}(
                    p_now timestamp with time zone, p_duration interval, p_batch integer, p_candidates integer,
                    p_global integer, p_tenants uuid[], p_tenant_caps integer[], p_endpoint_caps integer[],
                    p_cycles integer[], p_target uuid)
                RETURNS TABLE ("TenantId" uuid, "TargetId" uuid, "LeaseToken" uuid, "DeliveryFence" bigint,
                    "ClaimedAtUtc" timestamp with time zone, "LeaseExpiresAtUtc" timestamp with time zone,
                    "NextActionAtUtc" timestamp with time zone)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE
                    v_capacity integer; v_size integer := cardinality(p_tenants); v_per_tenant integer;
                    v_candidates uuid[]; v_seen uuid[] := ARRAY[]::uuid[];
                    v_selected integer[]; v_total integer := 0; v_added boolean;
                    v_index integer; v_tenant_count integer; v_endpoint_count integer;
                    v_candidate record; v_token uuid; v_changed integer;
                BEGIN
                    {invocation}
                    IF p_now IS NULL OR NOT isfinite(p_now) OR p_duration IS NULL OR p_duration <= interval '0 seconds'
                       OR p_duration > interval '1 hour' OR p_batch IS NULL OR p_batch NOT BETWEEN 1 AND 1000
                       OR p_candidates IS NULL OR p_candidates NOT BETWEEN p_batch AND 10000
                       OR p_global IS NULL OR p_global NOT BETWEEN 1 AND 256
                       OR v_size IS NULL OR v_size NOT BETWEEN 1 AND 10000
                       OR cardinality(p_tenant_caps) IS DISTINCT FROM v_size
                       OR cardinality(p_endpoint_caps) IS DISTINCT FROM v_size
                       OR cardinality(p_cycles) IS DISTINCT FROM v_size
                       OR array_lower(p_tenants, 1) <> 1 OR array_lower(p_tenant_caps, 1) <> 1
                       OR array_lower(p_endpoint_caps, 1) <> 1 OR array_lower(p_cycles, 1) <> 1
                       OR EXISTS (SELECT 1 FROM unnest(p_tenants) t WHERE t IS NULL OR t = '00000000-0000-0000-0000-000000000000'::uuid)
                       OR (SELECT count(DISTINCT t) FROM unnest(p_tenants) t) <> v_size
                       OR EXISTS (SELECT 1 FROM unnest(p_tenant_caps) c WHERE c IS NULL OR c NOT BETWEEN 1 AND 256)
                       OR EXISTS (SELECT 1 FROM unnest(p_endpoint_caps) c WHERE c IS NULL OR c NOT BETWEEN 1 AND 256)
                       OR EXISTS (SELECT 1 FROM unnest(p_cycles) c WHERE c IS NULL OR c NOT BETWEEN 1 AND 1000) THEN
                        RAISE EXCEPTION 'invalid local webhook claim bounds' USING ERRCODE = '22023';
                    END IF;
                    PERFORM pg_advisory_xact_lock({lockKey});
                    SELECT LEAST(p_batch, p_global - count(*)::integer) INTO v_capacity FROM {targets} t
                        WHERE t.{status} = {delivering} AND t.{expires} > p_now;
                    IF v_capacity <= 0 THEN RETURN; END IF;
                    v_per_tenant := greatest(1, p_candidates / v_size);
                    v_selected := array_fill(0, ARRAY[v_size]);
                    SELECT coalesce(array_agg(c.id), ARRAY[]::uuid[]) INTO v_candidates FROM (
                        SELECT t.{id} AS id, row_number() OVER (
                            PARTITION BY t.{tenant} ORDER BY t.{next}, t.{captured}, t.{id}) AS position
                        FROM {targets} t JOIN {endpoints} e ON e.{endpointId} = t.{endpoint}
                        WHERE t.{tenant} = ANY(p_tenants) AND t.{status} IN ({pending}, {retry})
                          AND t.{next} <= p_now AND e.{endpointStatus} = {active}
                          AND (p_target IS NULL OR t.{id} = p_target)) c WHERE c.position <= v_per_tenant;
                    LOOP
                        v_added := false;
                        FOR v_index IN 1..v_size LOOP
                            SELECT count(*)::integer INTO v_tenant_count FROM {targets} t
                                WHERE t.{tenant} = p_tenants[v_index] AND t.{status} = {delivering} AND t.{expires} > p_now;
                            IF v_selected[v_index] >= p_cycles[v_index] OR v_tenant_count >= p_tenant_caps[v_index] THEN
                                CONTINUE;
                            END IF;
                            LOOP
                                SELECT t.{id} AS id, t.{endpoint} AS endpoint INTO v_candidate
                                    FROM {targets} t JOIN {endpoints} e ON e.{endpointId} = t.{endpoint}
                                    WHERE t.{tenant} = p_tenants[v_index] AND t.{id} = ANY(v_candidates)
                                      AND NOT (t.{id} = ANY(v_seen)) AND t.{status} IN ({pending}, {retry})
                                      AND t.{next} <= p_now AND e.{endpointStatus} = {active}
                                    ORDER BY t.{next}, t.{captured}, t.{id} LIMIT 1;
                                IF NOT FOUND THEN EXIT; END IF;
                                v_seen := array_append(v_seen, v_candidate.id);
                                SELECT count(*)::integer INTO v_endpoint_count FROM {targets} t
                                    WHERE t.{tenant} = p_tenants[v_index] AND t.{endpoint} = v_candidate.endpoint
                                      AND t.{status} = {delivering} AND t.{expires} > p_now;
                                IF v_endpoint_count >= p_endpoint_caps[v_index] THEN CONTINUE; END IF;
                                v_token := uuidv7();
                                RETURN QUERY UPDATE {targets} t SET {status} = {delivering},
                                    {owner} = 'webhook-delivery-worker', {lease} = v_token,
                                    {expires} = p_now + p_duration, {fence} = t.{fence} + 1,
                                    {version} = t.{version} + 1, {updated} = p_now
                                    WHERE t.{tenant} = p_tenants[v_index] AND t.{id} = v_candidate.id
                                      AND t.{status} IN ({pending}, {retry}) AND t.{next} <= p_now
                                    RETURNING t.{tenant}, t.{id}, t.{lease}, t.{fence}, p_now, t.{expires}, t.{next};
                                GET DIAGNOSTICS v_changed = ROW_COUNT;
                                IF v_changed > 0 THEN
                                    v_selected[v_index] := v_selected[v_index] + 1;
                                    v_total := v_total + 1;
                                    v_added := true;
                                    EXIT;
                                END IF;
                            END LOOP;
                            IF v_total >= v_capacity THEN RETURN; END IF;
                        END LOOP;
                        IF NOT v_added THEN RETURN; END IF;
                    END LOOP;
                END $body$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {recovery}(
                    p_now timestamp with time zone, p_category text, p_take integer)
                RETURNS integer LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_target record; v_count integer := 0;
                BEGIN
                    {invocation}
                    IF p_now IS NULL OR NOT isfinite(p_now) OR p_category IS NULL
                       OR p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000 THEN
                        RAISE EXCEPTION 'invalid local webhook recovery bounds' USING ERRCODE = '22023';
                    END IF;
                    PERFORM pg_advisory_xact_lock({lockKey});
                    FOR v_target IN SELECT t.{id} AS id, t.{tenant} AS tenant, t.{message} AS message,
                        t.{endpoint} AS endpoint, t.{fence} AS fence, coalesce(t.{updated}, t.{created}) AS started
                        FROM {targets} t WHERE t.{status} = {delivering} AND t.{expires} <= p_now
                        ORDER BY t.{expires}, t.{id} LIMIT p_take FOR UPDATE LOOP
                        INSERT INTO {attempts} ({evidenceColumns}) VALUES (
                            uuidv7(), v_target.tenant, v_target.message, v_target.endpoint,
                            v_target.fence::integer, {(int)WebhookDeliveryAttemptOutcome.Failed},
                            v_target.started, v_target.started, p_now, v_target.fence, left(p_category, 100), p_now);
                        UPDATE {targets} t SET {status} = CASE WHEN t.{fence} >= t.{maxAttempts} THEN {deadLettered} ELSE {retry} END,
                            {next} = p_now, {owner} = NULL, {lease} = NULL, {expires} = NULL,
                            {version} = t.{version} + 1, {updated} = p_now
                            WHERE t.{id} = v_target.id AND t.{tenant} = v_target.tenant;
                        v_count := v_count + 1;
                    END LOOP;
                    RETURN v_count;
                END $body$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {systemGovernance}()
                RETURNS SETOF {system} LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {invocation}
                    RETURN QUERY SELECT settings.* FROM {system} settings WHERE settings.{systemKey} IN ({keys});
                END $body$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {tenantGovernance}(p_tenant uuid)
                RETURNS SETOF {tenantSettings} LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {invocation}
                    IF p_tenant IS NULL OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid THEN
                        RAISE EXCEPTION 'invalid local webhook governance tenant' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY SELECT settings.* FROM {tenantSettings} settings
                        WHERE settings.{settingsTenant} = p_tenant AND settings.{key} IN ({keys});
                END $body$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signatures} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {signatures} TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $ready$ BEGIN {PostgresPrivilegeGuard.BuildAssertSql(contract)} END $ready$;
            """;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
