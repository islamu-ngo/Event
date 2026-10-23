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
/// Global WebPush authority can discover owners, claim due work, and recover stale leases.
/// The non-login definer has ledger-column grants only; private content stays under ordinary tenant authority.
/// </summary>
public static class PostgresWebPushDispatchAdmissionContract
{
    public const string OwnerRole = "event_webpush_admission_owner";
    public const string MigratorRole = "event_webpush_admission_migrator";
    public const string GlobalRole = "event_webpush_admission_global";
    public const string PolicyName = "webpush_dispatch_admission";

    public static string DiscoveryFunctionSql(ExploreDbContext context) => FunctionSql(context, "_pending_owners");
    public static string ClaimFunctionSql(ExploreDbContext context) => FunctionSql(context, "_claim");
    public static string RecoveryFunctionSql(ExploreDbContext context) => FunctionSql(context, "_recover");
    public static string CountFunctionSql(ExploreDbContext context) => FunctionSql(context, "_counts");

    private static string FunctionSql(ExploreDbContext context, string suffix)
    {
        var entity = context.Model.FindEntityType(typeof(WebPushDispatchOutbox))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()! + suffix, Schema(context, entity));
    }

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<WebPushDispatchOwner>> DiscoverAsync(
        ExploreDbContext context, int batchSize, DateTime now, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<WebPushDispatchOwner>(
            $"SELECT * FROM {DiscoveryFunctionSql(context)}({{0}}, {{1}})", now, batchSize)
            .ToListAsync(cancellationToken);

    internal static async Task<bool> ClaimAsync(
        ExploreDbContext context, WebPushDispatchOwner owner, Guid leaseToken, DateTime startedAt,
        CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<bool>(
            $"SELECT {ClaimFunctionSql(context)}({{0}}, {{1}}, {{2}}, {{3}}) AS \"Value\"",
            owner.TenantId, owner.Id, leaseToken, startedAt).SingleAsync(cancellationToken);

    internal static async Task<int> RecoverAsync(
        ExploreDbContext context, DateTime processingStartedBefore, DateTime recoveredAt,
        string failureCategory, string errorMessage, int batchSize, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<int>(
            $"SELECT {RecoveryFunctionSql(context)}({{0}}, {{1}}, {{2}}, {{3}}, {{4}}) AS \"Value\"",
            processingStartedBefore, recoveredAt, batchSize, failureCategory, errorMessage)
            .SingleAsync(cancellationToken);

    internal static async Task<int> CountAsync(
        ExploreDbContext context, int kind, DateTime? cutoff, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<int>(
            $"SELECT {CountFunctionSql(context)}(@kind, @cutoff) AS \"Value\"",
            new NpgsqlParameter("kind", kind),
            new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = (object?)cutoff ?? DBNull.Value })
            .SingleAsync(cancellationToken);

    public static string BuildSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(WebPushDispatchOutbox))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context, entity));
        string table = sql.DelimitIdentifier(mapping.Name, Schema(context, entity));
        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(mapping)!);
        string id = Column(nameof(WebPushDispatchOutbox.Id));
        string tenant = Column(nameof(WebPushDispatchOutbox.TenantId));
        string status = Column(nameof(WebPushDispatchOutbox.Status));
        string next = Column(nameof(WebPushDispatchOutbox.NextAttemptAt));
        string started = Column(nameof(WebPushDispatchOutbox.ProcessingStartedAt));
        string lease = Column(nameof(WebPushDispatchOutbox.ProcessingLeaseToken));
        string attempts = Column(nameof(WebPushDispatchOutbox.AttemptCount));
        string created = Column(nameof(WebPushDispatchOutbox.CreatedAt));
        string updated = Column(nameof(WebPushDispatchOutbox.UpdatedAt));
        string deleted = Column(nameof(WebPushDispatchOutbox.IsDeleted));
        string category = Column(nameof(WebPushDispatchOutbox.LastFailureCategory));
        string error = Column(nameof(WebPushDispatchOutbox.LastError));
        string failed = Column(nameof(WebPushDispatchOutbox.LastFailureAt));
        string discovery = DiscoveryFunctionSql(context);
        string claim = ClaimFunctionSql(context);
        string recovery = RecoveryFunctionSql(context);
        string counts = CountFunctionSql(context);
        string discoverySignature = $"{discovery}(timestamp with time zone, integer)";
        string claimSignature = $"{claim}(uuid, uuid, uuid, timestamp with time zone)";
        string recoverySignature = $"{recovery}(timestamp with time zone, timestamp with time zone, integer, text, text)";
        string countSignature = $"{counts}(integer, timestamp with time zone)";
        int pending = (int)WebPushDispatchStatus.Pending;
        int retry = (int)WebPushDispatchStatus.RetryScheduled;
        int processing = (int)WebPushDispatchStatus.Processing;
        int deadLettered = (int)WebPushDispatchStatus.DeadLettered;
        int permanentFailed = (int)WebPushDispatchStatus.PermanentFailed;
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
                    RAISE EXCEPTION 'WebPush Global and migration authority must remain separate';
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
                    RAISE EXCEPTION 'WebPush Global and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({tenant}, {id}, {status}, {next}, {started}, {lease}, {attempts}, {created}, {deleted})
                ON {table} TO {OwnerRole};
            GRANT UPDATE ({status}, {next}, {started}, {lease}, {attempts}, {updated}, {category}, {error}, {failed})
                ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {table};
            CREATE POLICY "{PolicyName}" ON {table} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {discovery}(
                    p_now timestamp with time zone, p_take integer)
                RETURNS TABLE ("TenantId" uuid, "Id" uuid)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE v_now timestamp with time zone := LEAST(p_now, statement_timestamp());
                BEGIN
                    IF p_now IS NULL OR NOT isfinite(p_now)
                       OR p_take IS NULL OR p_take NOT BETWEEN 1 AND {IWebPushDispatchAdmission.MaximumBatchSize} THEN
                        RAISE EXCEPTION 'invalid WebPush discovery bounds' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY SELECT queue.{tenant}, queue.{id} FROM {table} AS queue
                        WHERE NOT queue.{deleted} AND queue.{status} IN ({pending}, {retry})
                          AND (queue.{next} IS NULL OR queue.{next} <= v_now)
                        ORDER BY queue.{created}, queue.{id} LIMIT p_take;
                END $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {claim}(
                    p_tenant uuid, p_dispatch uuid, p_lease uuid, p_started timestamp with time zone)
                RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE
                    v_started timestamp with time zone := LEAST(p_started, statement_timestamp());
                    v_updated integer;
                BEGIN
                    IF p_tenant IS NULL OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                       OR p_dispatch IS NULL OR p_dispatch = '00000000-0000-0000-0000-000000000000'::uuid
                       OR p_lease IS NULL OR p_lease = '00000000-0000-0000-0000-000000000000'::uuid
                       OR p_started IS NULL OR NOT isfinite(p_started) THEN
                        RAISE EXCEPTION 'invalid WebPush claim identity' USING ERRCODE = '22023';
                    END IF;
                    UPDATE {table} AS queue SET {status} = {processing}, {started} = v_started,
                        {lease} = p_lease, {attempts} = queue.{attempts} + 1, {updated} = v_started
                        WHERE queue.{tenant} = p_tenant AND queue.{id} = p_dispatch
                          AND NOT queue.{deleted} AND queue.{status} IN ({pending}, {retry})
                          AND (queue.{next} IS NULL OR queue.{next} <= v_started);
                    GET DIAGNOSTICS v_updated = ROW_COUNT;
                    RETURN v_updated > 0;
                END $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {recovery}(
                    p_before timestamp with time zone, p_recovered timestamp with time zone,
                    p_take integer, p_category text, p_error text)
                RETURNS integer LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE
                    v_recovered timestamp with time zone := LEAST(p_recovered, statement_timestamp());
                    v_updated integer;
                BEGIN
                    IF p_before IS NULL OR NOT isfinite(p_before)
                       OR p_recovered IS NULL OR NOT isfinite(p_recovered) OR p_before > p_recovered
                       OR p_take IS NULL OR p_take NOT BETWEEN 1 AND {IWebPushDispatchAdmission.MaximumBatchSize}
                       OR p_category IS NULL OR p_error IS NULL THEN
                        RAISE EXCEPTION 'invalid WebPush recovery bounds' USING ERRCODE = '22023';
                    END IF;
                    WITH stale AS (
                        SELECT queue.{id} FROM {table} AS queue
                        WHERE NOT queue.{deleted} AND queue.{status} = {processing}
                          AND queue.{started} IS NOT NULL
                          AND queue.{started} <= LEAST(p_before, v_recovered)
                        ORDER BY queue.{started}, queue.{id} LIMIT p_take FOR UPDATE SKIP LOCKED)
                    UPDATE {table} AS queue SET {status} = {retry}, {next} = v_recovered,
                        {started} = NULL, {lease} = NULL, {category} = left(p_category, 100),
                        {error} = left(p_error, 2000), {failed} = v_recovered, {updated} = v_recovered
                        FROM stale WHERE queue.{id} = stale.{id}
                          AND NOT queue.{deleted} AND queue.{status} = {processing}
                          AND queue.{started} IS NOT NULL
                          AND queue.{started} <= LEAST(p_before, v_recovered);
                    GET DIAGNOSTICS v_updated = ROW_COUNT;
                    RETURN v_updated;
                END $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {counts}(
                    p_kind integer, p_cutoff timestamp with time zone)
                RETURNS integer LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE v_count integer;
                BEGIN
                    IF p_kind IS NULL OR p_kind NOT BETWEEN 1 AND 4
                       OR (p_kind IN (1, 3) AND (p_cutoff IS NULL OR NOT isfinite(p_cutoff)))
                       OR (p_kind IN (2, 4) AND p_cutoff IS NOT NULL) THEN
                        RAISE EXCEPTION 'invalid WebPush aggregate bounds' USING ERRCODE = '22023';
                    END IF;
                    SELECT count(*)::integer INTO v_count FROM {table} AS queue
                    WHERE NOT queue.{deleted} AND CASE p_kind
                        WHEN 1 THEN queue.{status} IN ({pending}, {retry})
                            AND (queue.{next} IS NULL OR queue.{next} <= p_cutoff)
                        WHEN 2 THEN queue.{status} = {retry}
                        WHEN 3 THEN queue.{status} = {processing} AND queue.{started} IS NOT NULL
                            AND queue.{started} <= p_cutoff
                        WHEN 4 THEN queue.{status} IN ({deadLettered}, {permanentFailed})
                        ELSE false END;
                    RETURN v_count;
                END $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {discoverySignature}, {claimSignature}, {recoverySignature}, {countSignature}
                        FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {discoverySignature}, {claimSignature}, {recoverySignature}, {countSignature}
                        TO {GlobalRole};
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
