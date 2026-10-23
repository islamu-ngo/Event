using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Migration-owned execute-only claim authority. Its non-login owner can change
/// lease columns on finalization effects, not orders, money, capacity or receipts.
/// </summary>
public static partial class PostgresRegistrationFinalizationQueueContract
{
    public const string OwnerRole = "event_registration_finalization_owner";
    public const string MigratorRole = "event_registration_finalization_migrator";
    public const string GlobalRole = "event_registration_finalization_global";
    public const string PolicyName = "registration_finalization_queue_authority";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("registration_finalization_claim", Schema(context));

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
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
        var contract = PrivilegeContract(context, enrollment);
        string assertion = PostgresPrivilegeGuard.BuildAssertSql(contract) + PostgresPrivilegeGuard.RestrictedCallerSql;
        var sql = context.GetService<ISqlGenerationHelper>();
        var entity = context.Model.FindEntityType(typeof(RegistrationFinalizationEffect))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string C(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!);
        string schema = sql.DelimitIdentifier(Schema(context));
        string table = sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
        string id = C(nameof(RegistrationFinalizationEffect.Id));
        string tenant = C(nameof(RegistrationFinalizationEffect.TenantId));
        string order = C(nameof(RegistrationFinalizationEffect.RegistrationOrderId));
        string status = C(nameof(RegistrationFinalizationEffect.Status));
        string next = C(nameof(RegistrationFinalizationEffect.NextAttemptAt));
        string expires = C(nameof(RegistrationFinalizationEffect.ProcessingLeaseExpiresAt));
        string token = C(nameof(RegistrationFinalizationEffect.ProcessingLeaseToken));
        string owner = C(nameof(RegistrationFinalizationEffect.ProcessingLeaseOwner));
        string fence = C(nameof(RegistrationFinalizationEffect.ProcessingFence));
        string attempt = C(nameof(RegistrationFinalizationEffect.AttemptCount));
        string created = C(nameof(RegistrationFinalizationEffect.CreatedAt));
        string updated = C(nameof(RegistrationFinalizationEffect.UpdatedAt));
        string function = FunctionSql(context);
        string signature = $"{function}(text, integer, timestamp with time zone, interval)";
        return $"""
            {EnrollmentSql(contract, enrollment)}
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({id}, {tenant}, {order}, {status}, {next}, {expires}, {fence}, {attempt}, {created})
                ON {table} TO {OwnerRole};
            GRANT UPDATE ({status}, {next}, {expires}, {token}, {owner}, {fence}, {attempt}, {updated})
                ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {table};
            CREATE POLICY "{PolicyName}" ON {table} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(
                    p_owner text, p_take integer, p_observed timestamp with time zone, p_duration interval)
                RETURNS TABLE (effect_id uuid, tenant_id uuid, registration_order_id uuid,
                    lease_token uuid, processing_fence bigint)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE q record; v_token uuid;
                BEGIN
                    {assertion}
                    IF session_user IS DISTINCT FROM {Text(enrollment.GlobalLogin)} THEN
                        RAISE EXCEPTION 'registration_finalization_session_not_enrolled' USING ERRCODE = '42501';
                    END IF;
                    IF p_owner IS NULL OR length(btrim(p_owner)) NOT BETWEEN 1 AND {RegistrationFinalizationEffect.MaxLeaseOwnerLength}
                       OR p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000
                       OR p_observed IS NULL OR NOT isfinite(p_observed)
                       OR p_duration IS NULL OR NOT isfinite(p_duration) OR p_duration <= interval '0' THEN
                        RAISE EXCEPTION 'invalid finalization claim bounds' USING ERRCODE = '22023';
                    END IF;
                    FOR q IN
                        SELECT queue.{id} AS effect_id, queue.{tenant} AS tenant_id,
                            queue.{order} AS order_id, queue.{fence} AS fence, queue.{attempt} AS attempt
                        FROM {table} AS queue
                        WHERE (queue.{status} IN ({(int)OutboxMessageStatus.Pending}, {(int)OutboxMessageStatus.Failed})
                               AND (queue.{next} IS NULL OR queue.{next} <= p_observed))
                           OR (queue.{status} = {(int)OutboxMessageStatus.Processing}
                               AND queue.{expires} <= p_observed)
                        ORDER BY coalesce(queue.{next}, queue.{created}), queue.{id}
                        LIMIT p_take FOR UPDATE SKIP LOCKED
                    LOOP
                        v_token := uuidv7();
                        UPDATE {table} AS queue SET {status} = {(int)OutboxMessageStatus.Processing},
                            {next} = NULL, {owner} = btrim(p_owner), {token} = v_token,
                            {expires} = p_observed + p_duration, {fence} = q.fence + 1,
                            {attempt} = q.attempt + 1, {updated} = p_observed
                            WHERE queue.{tenant} = q.tenant_id AND queue.{id} = q.effect_id;
                        effect_id := q.effect_id;
                        tenant_id := q.tenant_id;
                        registration_order_id := q.order_id;
                        lease_token := v_token;
                        processing_fence := q.fence + 1;
                        RETURN NEXT;
                    END LOOP;
                END $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $ready$ BEGIN {PostgresPrivilegeGuard.BuildAssertSql(contract)} END $ready$;
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(RegistrationFinalizationEffect))!.GetSchema() ?? "public";
}
