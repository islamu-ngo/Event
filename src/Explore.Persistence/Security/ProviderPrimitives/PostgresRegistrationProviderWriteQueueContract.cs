using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Claims metadata without granting Global access to submission answers or storage content.</summary>
public static partial class PostgresRegistrationProviderWriteQueueContract
{
    public const string OwnerRole = "event_registration_provider_write_owner";
    public const string MigratorRole = "event_registration_provider_write_migrator";
    public const string GlobalRole = "event_registration_provider_write_global";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("registration_provider_write_claim", Schema(context));

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
        var contract = PrivilegeContract(context, enrollment);
        string assertion = PostgresPrivilegeGuard.BuildAssertSql(contract) + PostgresPrivilegeGuard.RestrictedCallerSql;
        var sql = context.GetService<ISqlGenerationHelper>();
        var effect = context.Model.FindEntityType(typeof(RegistrationProviderSubmissionWriteEffect))!;
        var storage = context.Model.FindEntityType(typeof(StorageObject))!;
        string E(string property) => sql.DelimitIdentifier(effect.FindProperty(property)!.GetColumnName(
            StoreObjectIdentifier.Table(effect.GetTableName()!, effect.GetSchema()))!);
        string S(string property) => sql.DelimitIdentifier(storage.FindProperty(property)!.GetColumnName(
            StoreObjectIdentifier.Table(storage.GetTableName()!, storage.GetSchema()))!);
        string schema = sql.DelimitIdentifier(Schema(context));
        string table = sql.DelimitIdentifier(effect.GetTableName()!, Schema(context));
        string objects = sql.DelimitIdentifier(storage.GetTableName()!, Schema(context));
        string function = FunctionSql(context);
        string signature = $"{function}(text, integer, timestamp with time zone, interval)";
        long claimLockKey = RelationalNamedLock.ComputeStableKey("registration-provider-submission-write-claim");
        string id = E(nameof(RegistrationProviderSubmissionWriteEffect.Id));
        string tenant = E(nameof(RegistrationProviderSubmissionWriteEffect.TenantId));
        string submission = E(nameof(RegistrationProviderSubmissionWriteEffect.RegistrationSubmissionId));
        string attemptId = E(nameof(RegistrationProviderSubmissionWriteEffect.RegistrationAttemptId));
        string binding = E(nameof(RegistrationProviderSubmissionWriteEffect.RegistrationProviderBindingId));
        string status = E(nameof(RegistrationProviderSubmissionWriteEffect.Status));
        string next = E(nameof(RegistrationProviderSubmissionWriteEffect.NextAttemptAt));
        string expires = E(nameof(RegistrationProviderSubmissionWriteEffect.ProcessingLeaseExpiresAt));
        string token = E(nameof(RegistrationProviderSubmissionWriteEffect.ProcessingLeaseToken));
        string owner = E(nameof(RegistrationProviderSubmissionWriteEffect.ProcessingLeaseOwner));
        string fence = E(nameof(RegistrationProviderSubmissionWriteEffect.ProcessingFence));
        string count = E(nameof(RegistrationProviderSubmissionWriteEffect.AttemptCount));
        string created = E(nameof(RegistrationProviderSubmissionWriteEffect.CreatedAt));
        string updated = E(nameof(RegistrationProviderSubmissionWriteEffect.UpdatedAt));
        string sid = S(nameof(StorageObject.Id)), stenant = S(nameof(StorageObject.TenantId));
        string lineage = S(nameof(StorageObject.OwningResourceId)), kind = S(nameof(StorageObject.OwningResourceKind));
        string stamp = S(nameof(StorageObject.ConcurrencyStamp));
        string eligible = $"((pending.{status} IN ({(int)OutboxMessageStatus.Pending}, {(int)OutboxMessageStatus.Failed}) "
            + $"AND (pending.{next} IS NULL OR pending.{next} <= v_at)) OR "
            + $"(pending.{status} = {(int)OutboxMessageStatus.Processing} AND pending.{expires} <= v_at))";
        return $"""
            {EnrollmentSql(contract, enrollment)}
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {table}, {objects} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({id}, {tenant}, {submission}, {attemptId}, {binding}, {status}, {next},
                {expires}, {fence}, {count}, {created}) ON {table} TO {OwnerRole};
            GRANT UPDATE ({status}, {next}, {expires}, {token}, {owner}, {fence}, {count}, {updated}) ON {table} TO {OwnerRole};
            GRANT SELECT ({sid}, {stenant}, {lineage}, {kind}) ON {objects} TO {OwnerRole};
            GRANT UPDATE ({stamp}) ON {objects} TO {OwnerRole};
            DROP POLICY IF EXISTS registration_provider_write_queue ON {table};
            CREATE POLICY registration_provider_write_queue ON {table} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS registration_provider_write_storage_fence ON {objects};
            CREATE POLICY registration_provider_write_storage_fence ON {objects} FOR ALL TO {OwnerRole}
                USING ({kind} = 'registration_submission_sink') WITH CHECK ({kind} = 'registration_submission_sink');
            DO $bootstrap$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(
                    p_owner text, p_take integer, p_observed timestamp with time zone, p_duration interval)
                RETURNS TABLE (effect_id uuid, tenant_id uuid, registration_submission_id uuid,
                    registration_attempt_id uuid, registration_provider_binding_id uuid, lease_token uuid,
                    processing_fence bigint, attempt_count integer, lease_expires_at timestamp with time zone)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE v_at timestamp with time zone; candidates jsonb; q record; s record; minted uuid;
                BEGIN
                    {assertion}
                    IF session_user IS DISTINCT FROM {Text(enrollment.GlobalLogin)} THEN
                        RAISE EXCEPTION 'registration_provider_write_session_not_enrolled' USING ERRCODE = '42501';
                    END IF;
                    IF p_owner IS NULL OR length(btrim(p_owner)) NOT BETWEEN 1 AND {RegistrationProviderSubmissionWriteEffect.MaxLeaseOwnerLength}
                        OR p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000
                        OR p_observed IS NULL OR NOT isfinite(p_observed)
                        OR p_duration IS NULL OR NOT isfinite(p_duration) OR p_duration <= interval '0' THEN
                        RAISE EXCEPTION 'invalid provider write claim bounds' USING ERRCODE = '22023';
                    END IF;
                    v_at := LEAST(p_observed, statement_timestamp());
                    PERFORM pg_advisory_xact_lock({claimLockKey.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                    SELECT jsonb_agg(to_jsonb(candidate)) INTO candidates FROM (
                        SELECT pending.{id} AS id, pending.{tenant} AS tenant, pending.{submission} AS submission,
                            pending.{attemptId} AS attempt, pending.{binding} AS binding
                        FROM {table} pending WHERE {eligible}
                        ORDER BY coalesce(pending.{next}, pending.{created}), pending.{id} LIMIT p_take
                    ) candidate;
                    IF candidates IS NULL THEN RETURN; END IF;
                    -- Match the ordinary writer's storage-before-effect lock order.
                    FOR s IN SELECT DISTINCT object.{sid} AS id, object.{stenant} AS tenant
                        FROM jsonb_to_recordset(candidates) AS candidate(id uuid, tenant uuid, submission uuid, attempt uuid, binding uuid)
                        JOIN {objects} object ON object.{stenant} = candidate.tenant
                            AND object.{lineage} = candidate.submission AND object.{kind} = 'registration_submission_sink'
                        ORDER BY tenant, id
                    LOOP
                        UPDATE {objects} object SET {stamp} = uuidv7()
                        WHERE object.{stenant} = s.tenant AND object.{sid} = s.id;
                    END LOOP;
                    FOR q IN SELECT queue.{id} AS id, queue.{tenant} AS tenant, queue.{submission} AS submission,
                            queue.{attemptId} AS attempt, queue.{binding} AS binding,
                            queue.{fence} AS fence, queue.{count} AS attempts
                        FROM {table} queue JOIN jsonb_to_recordset(candidates)
                            AS candidate(id uuid, tenant uuid, submission uuid, attempt uuid, binding uuid)
                            ON queue.{id} = candidate.id AND queue.{tenant} = candidate.tenant
                                AND queue.{submission} = candidate.submission AND queue.{attemptId} = candidate.attempt
                                AND queue.{binding} = candidate.binding
                        WHERE ((queue.{status} IN ({(int)OutboxMessageStatus.Pending}, {(int)OutboxMessageStatus.Failed})
                                AND (queue.{next} IS NULL OR queue.{next} <= v_at))
                            OR (queue.{status} = {(int)OutboxMessageStatus.Processing} AND queue.{expires} <= v_at))
                        ORDER BY coalesce(queue.{next}, queue.{created}), queue.{id} FOR UPDATE OF queue SKIP LOCKED
                    LOOP
                        minted := uuidv7();
                        UPDATE {table} queue SET {status} = {(int)OutboxMessageStatus.Processing}, {next} = NULL,
                            {owner} = btrim(p_owner), {token} = minted, {expires} = v_at + p_duration,
                            {fence} = q.fence + 1, {count} = q.attempts + 1, {updated} = v_at
                        WHERE queue.{tenant} = q.tenant AND queue.{id} = q.id;
                        effect_id := q.id; tenant_id := q.tenant; registration_submission_id := q.submission;
                        registration_attempt_id := q.attempt; registration_provider_binding_id := q.binding;
                        lease_token := minted; processing_fence := q.fence + 1; attempt_count := q.attempts + 1;
                        lease_expires_at := v_at + p_duration;
                        RETURN NEXT;
                    END LOOP;
                END $function$ $ddl$;
                EXECUTE $acl$ REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {GlobalRole}; $acl$;
                PERFORM set_config('role', previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $ready$ BEGIN {PostgresPrivilegeGuard.BuildAssertSql(contract)} END $ready$;
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(RegistrationProviderSubmissionWriteEffect))!.GetSchema() ?? "public";
}
