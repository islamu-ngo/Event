using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Global can execute bounded claim/identity functions, not read source rows.
/// The non-login definer has column-limited source custody and outbox claim rights.
/// </summary>
public static class PostgresNotificationOutboxAdmissionContract
{
    public const string OwnerRole = "event_notification_outbox_owner";
    public const string MigratorRole = "event_notification_outbox_migrator";
    public const string GlobalRole = "event_notification_outbox_global";
    private const string ClaimColumns = """
        "MessageId" AS message_id, "TenantId" AS tenant_id, "SourceId" AS source_id,
        "AggregateId" AS aggregate_id, "AggregateType" AS aggregate_type, "EventType" AS event_type,
        "PayloadDigest" AS payload_digest, "ProcessingLeaseExpiresAt" AS processing_lease_expires_at
        """;

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";

    private static string Function(ExploreDbContext context, string suffix)
    {
        var entity = context.Model.FindEntityType(typeof(OutboxMessage))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_notification_" + suffix, Schema(context, entity));
    }

    public static string ClaimFunctionSql(ExploreDbContext context) => Function(context, "admit");
    public static string ReadFunctionSql(ExploreDbContext context) => Function(context, "claim");

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<NotificationOutboxClaim>> ClaimAsync(
        ExploreDbContext context, int batchSize, DateTime claimedAt, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<NotificationOutboxClaim>(
            $"SELECT {ClaimColumns} FROM {ClaimFunctionSql(context)}({{0}}, {{1}})", batchSize, claimedAt)
            .ToListAsync(cancellationToken);

    internal static Task<NotificationOutboxClaim?> ReadAsync(
        ExploreDbContext context, Guid messageId, DateTime lease, CancellationToken cancellationToken) =>
        context.Database.SqlQueryRaw<NotificationOutboxClaim>(
            $"SELECT {ClaimColumns} FROM {ReadFunctionSql(context)}({{0}}, {{1}})", messageId, lease)
            .SingleOrDefaultAsync(cancellationToken);

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table<T>()
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.GetTableName()!, Schema(context, entity));
        }
        string schema = sql.DelimitIdentifier(Schema(context, context.Model.FindEntityType(typeof(OutboxMessage))!));
        string outbox = Table<OutboxMessage>();
        string events = Table<Explore.Domain.Event>();
        string records = Table<EventModerationRecord>();
        string occurrences = Table<NotificationFanoutOccurrence>();
        string audits = Table<AuditLog>();
        string admit = ClaimFunctionSql(context);
        string read = ReadFunctionSql(context);
        string[] routines =
        [
            $"{read}(uuid, timestamp with time zone)",
            $"{admit}(integer, timestamp with time zone)"
        ];
        IReadOnlyDictionary<string, IReadOnlyList<string>> nestedExecutors =
            new Dictionary<string, IReadOnlyList<string>>
            {
                [routines[0]] = [PostgresOutboxExecutionContract.OwnerRole, PostgresPublicationFanoutExecutionContract.OwnerRole]
            };
        string acl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(routines, OwnerRole, GlobalRole,
            requireComplete: true, additionalExecutors: nestedExecutors);
        string publication = EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType;
        string light = EventModerationOutboxMessageFactory.EventLightModeratedNotificationFanoutRequestedEventType;
        string heavy = EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType;
        string correction = EventDiscoveryIdentityCorrectionRequested.EventType;
        string occurrence = NotificationFanoutOccurrenceOutboxMessageFactory.EventType;
        string resultColumns = """
            "MessageId" uuid, "TenantId" uuid, "SourceId" uuid, "AggregateId" uuid,
            "AggregateType" text, "EventType" text, "PayloadDigest" text,
            "ProcessingLeaseExpiresAt" timestamp with time zone
            """;
        return $"""
            DO $acl_preflight$ BEGIN
                {PostgresPrivilegeGuard.BuildRoutineAclAssertSql(routines, OwnerRole, GlobalRole,
                    requireComplete: false, additionalExecutors: nestedExecutors)}
            END $acl_preflight$;
            DO $roles$
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
                    RAISE EXCEPTION 'notification outbox admission and migration authority must remain separate';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT roleid FROM pg_catalog.pg_auth_members
                        WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        INNER JOIN inherited_roles inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'notification outbox admission and migration logins must remain separate';
                END IF;
            END $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {outbox}, {events}, {records}, {occurrences}, {audits} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT (id, aggregate_id, aggregate_type, event_type, payload, status, next_retry_at, created_at)
                ON {outbox} TO {OwnerRole};
            GRANT UPDATE (status, next_retry_at, last_error) ON {outbox} TO {OwnerRole};
            GRANT SELECT (id, tenant_id, is_deleted) ON {events} TO {OwnerRole};
            GRANT SELECT (id, tenant_id, event_id, action_kind, is_irreversible) ON {records} TO {OwnerRole};
            GRANT SELECT (id, tenant_id) ON {occurrences} TO {OwnerRole};
            GRANT SELECT (id, tenant_id, entity_type, entity_id, action, timestamp, new_values) ON {audits} TO {OwnerRole};
            DROP POLICY IF EXISTS notification_outbox_admission ON {events};
            CREATE POLICY notification_outbox_admission ON {events} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS notification_outbox_admission ON {records};
            CREATE POLICY notification_outbox_admission ON {records} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS notification_outbox_admission ON {occurrences};
            CREATE POLICY notification_outbox_admission ON {occurrences} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS notification_outbox_admission ON {audits};
            CREATE POLICY notification_outbox_admission ON {audits} FOR SELECT TO {OwnerRole}
                USING (entity_type = 'EventDiscoveryIdentity'
                    AND action IN ('EventDiscoveryAliasConfirmed', 'EventDiscoveryAliasUnlinked', 'EventDiscoveryIndependentReview'));
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {read}(p_id uuid, p_lease timestamp with time zone)
                RETURNS TABLE ({resultColumns})
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE r record; v_tenant uuid; v_posted_tenant uuid; v_source uuid; v_pointer uuid;
                    v_receipts uuid[]; v_receipt_tenants uuid[];
                BEGIN
                    {acl}
                    IF p_id IS NULL OR p_lease IS NULL OR NOT isfinite(p_lease)
                        OR p_lease <= statement_timestamp() THEN RETURN; END IF;
                    SELECT id, aggregate_id, aggregate_type, event_type, payload, next_retry_at, created_at INTO r
                    FROM {outbox} WHERE id = p_id AND status = {(int)OutboxMessageStatus.Processing}
                        AND next_retry_at = p_lease
                        AND event_type IN ('{publication}', '{light}', '{heavy}', '{occurrence}', '{correction}');
                    IF NOT FOUND OR r.payload IS NULL OR json_typeof(r.payload::json) <> 'object' THEN RETURN; END IF;
                    IF EXISTS (SELECT 1 FROM json_each(r.payload::json) GROUP BY lower(key) HAVING count(*) > 1) THEN RETURN; END IF;
                    SELECT (value #>> ARRAY[]::text[])::uuid INTO v_posted_tenant
                        FROM json_each(r.payload::json) WHERE lower(key) = 'tenantid';
                    IF r.event_type = '{correction}' AND r.aggregate_type = 'EventDiscoveryIdentity' THEN
                        SELECT (value #>> ARRAY[]::text[])::uuid INTO v_pointer
                            FROM json_each(r.payload::json) WHERE lower(key) = 'eventid';
                        IF v_pointer IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT array_agg(receipt.id), array_agg(receipt.tenant_id)
                            INTO v_receipts, v_receipt_tenants
                        FROM (
                            SELECT a.id, a.tenant_id FROM {audits} AS a
                            WHERE a.tenant_id = v_posted_tenant
                              AND a.entity_type = 'EventDiscoveryIdentity'
                              AND a.entity_id = r.aggregate_id::text
                              AND a.action IN ('EventDiscoveryAliasConfirmed', 'EventDiscoveryAliasUnlinked', 'EventDiscoveryIndependentReview')
                              AND a.timestamp = r.created_at AND a.new_values = r.payload::jsonb
                            LIMIT 2) AS receipt;
                        IF cardinality(v_receipts) = 1 THEN
                            v_source := v_receipts[1]; v_tenant := v_receipt_tenants[1];
                        END IF;
                    ELSIF r.event_type = '{occurrence}' AND r.aggregate_type = 'NotificationFanoutOccurrence' THEN
                        SELECT (value #>> ARRAY[]::text[])::uuid INTO v_pointer
                            FROM json_each(r.payload::json) WHERE lower(key) = 'occurrenceid';
                        IF v_pointer IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        v_source := r.aggregate_id;
                        SELECT tenant_id INTO v_tenant FROM {occurrences} WHERE id = v_source;
                    ELSIF r.aggregate_type = 'Event' AND r.event_type = '{publication}' THEN
                        SELECT (value #>> ARRAY[]::text[])::uuid INTO v_pointer
                            FROM json_each(r.payload::json) WHERE lower(key) = 'eventid';
                        IF v_pointer IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        v_source := r.aggregate_id;
                        SELECT tenant_id INTO v_tenant FROM {events} WHERE id = v_source AND NOT is_deleted;
                    ELSIF r.aggregate_type = 'Event' AND r.event_type IN ('{light}', '{heavy}') THEN
                        SELECT (value #>> ARRAY[]::text[])::uuid INTO v_source
                            FROM json_each(r.payload::json) WHERE lower(key) = 'moderationrecordid';
                        IF r.event_type = '{light}' THEN
                            SELECT (value #>> ARRAY[]::text[])::uuid INTO v_pointer
                                FROM json_each(r.payload::json) WHERE lower(key) = 'eventid';
                            IF v_pointer IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        END IF;
                        SELECT tenant_id INTO v_tenant FROM {records}
                            WHERE id = v_source AND event_id = r.aggregate_id
                                AND action_kind = CASE WHEN r.event_type = '{heavy}'
                                    THEN {(int)EventModerationActionKind.HeavyRedacted}
                                    ELSE {(int)EventModerationActionKind.LightModerated} END
                                AND is_irreversible = (r.event_type = '{heavy}');
                    END IF;
                    IF v_tenant IS NULL OR v_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                        OR v_posted_tenant IS DISTINCT FROM v_tenant THEN RETURN; END IF;
                    RETURN QUERY SELECT r.id, v_tenant, v_source, r.aggregate_id,
                        r.aggregate_type::text, r.event_type::text,
                        encode(sha256(convert_to(r.payload::text, 'UTF8')), 'hex'), r.next_retry_at;
                EXCEPTION WHEN invalid_text_representation THEN RETURN;
                END $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {admit}(p_take integer, p_claimed timestamp with time zone)
                RETURNS TABLE ({resultColumns})
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE r record; admitted record;
                    v_now timestamp with time zone := LEAST(p_claimed, statement_timestamp());
                    v_lease timestamp with time zone;
                BEGIN
                    {acl}
                    IF p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000 OR p_claimed IS NULL OR NOT isfinite(p_claimed) THEN
                        RAISE EXCEPTION 'invalid notification outbox admission bounds' USING ERRCODE = '22023';
                    END IF;
                    v_lease := v_now + interval '5 minutes';
                    FOR r IN SELECT id FROM {outbox}
                        WHERE event_type IN ('{publication}', '{light}', '{heavy}', '{occurrence}', '{correction}')
                            AND ((status = {(int)OutboxMessageStatus.Pending} AND (next_retry_at IS NULL OR next_retry_at <= v_now))
                                OR (status = {(int)OutboxMessageStatus.Processing} AND next_retry_at <= v_now))
                        ORDER BY created_at, id LIMIT p_take FOR UPDATE SKIP LOCKED
                    LOOP
                        UPDATE {outbox} SET status = {(int)OutboxMessageStatus.Processing}, next_retry_at = v_lease WHERE id = r.id;
                        SELECT * INTO admitted FROM {read}(r.id, v_lease);
                        IF FOUND THEN
                            RETURN QUERY SELECT admitted."MessageId", admitted."TenantId", admitted."SourceId",
                                admitted."AggregateId", admitted."AggregateType", admitted."EventType",
                                admitted."PayloadDigest", admitted."ProcessingLeaseExpiresAt";
                        ELSE
                            UPDATE {outbox} SET status = {(int)OutboxMessageStatus.Failed}, next_retry_at = NULL,
                                last_error = 'notification_source_not_admitted' WHERE id = r.id;
                        END IF;
                    END LOOP;
                END $function$ $ddl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE ALL ON FUNCTION {read}(uuid, timestamp with time zone) FROM PUBLIC;
            REVOKE ALL ON FUNCTION {admit}(integer, timestamp with time zone) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {read}(uuid, timestamp with time zone) TO {GlobalRole};
            GRANT EXECUTE ON FUNCTION {admit}(integer, timestamp with time zone) TO {GlobalRole};
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $acl_exit$ BEGIN {acl} END $acl_exit$;
            """;
    }
}
