using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Bounded generic queue admission. Global receives identity tuples through functions;
/// the non-login definer reads only source identity columns and the claimed envelope.
/// </summary>
public static class PostgresGenericOutboxAdmissionContract
{
    public const string OwnerRole = "event_generic_outbox_owner";
    public const string MigratorRole = "event_generic_outbox_migrator";
    public const string GlobalRole = "event_generic_outbox_global";
    private const string ClaimColumns = """
        "MessageId" AS message_id, "TenantId" AS tenant_id, "Ownership" AS ownership,
        "SourceId" AS source_id, "SourceIdentity" AS source_identity, "AggregateId" AS aggregate_id,
        "AggregateType" AS aggregate_type, "EventType" AS event_type, "PayloadDigest" AS payload_digest,
        "CreatedAt" AS created_at, "Status" AS status, "LeaseExpiresAt" AS lease_expires_at
        """;

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";

    private static string Function(ExploreDbContext context, string suffix)
    {
        var entity = context.Model.FindEntityType(typeof(OutboxMessage))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_generic_" + suffix, Schema(context, entity));
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

    internal static async Task<IReadOnlyList<GenericOutboxClaim>> ClaimAsync(
        ExploreDbContext context, int take, DateTime claimedAt, CancellationToken ct) =>
        await context.Database.SqlQueryRaw<GenericOutboxClaim>(
            $"SELECT {ClaimColumns} FROM {ClaimFunctionSql(context)}({{0}}, {{1}})", take, claimedAt).ToListAsync(ct);

    internal static Task<GenericOutboxClaim?> ReadAsync(
        ExploreDbContext context, Guid id, DateTime expiry, OutboxMessageStatus status, CancellationToken ct) =>
        context.Database.SqlQueryRaw<GenericOutboxClaim>(
            $"SELECT {ClaimColumns} FROM {ReadFunctionSql(context)}({{0}}, {{1}}, {{2}})", id, expiry, (int)status)
            .SingleOrDefaultAsync(ct);

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
        string reports = Table<EventReport>();
        string cases = Table<EventReportCase>();
        string locations = Table<EventLocation>();
        string audits = Table<EventLocationDisclosureAudit>();
        string correctionReceipts = Table<LocationPrivacyCorrectionReceipt>();
        string orders = Table<RegistrationOrder>();
        string delivery = Table<AdmissionDeliveryIntent>();
        string recoveryRequest = Table<AdmissionRecoveryRequestIntent>();
        string recoveryDelivery = Table<AdmissionRecoveryDeliveryIntent>();
        string campaigns = Table<RefundCampaign>();
        string refunds = Table<RefundAttempt>();
        string payments = Table<PaymentAttempt>();
        string manifests = Table<ConfigurationManifestOperation>();
        string imports = Table<ConfigurationImportOperation>();
        string provisioning = Table<ManagedTenantProvisioningOperation>();
        string admit = ClaimFunctionSql(context);
        string read = ReadFunctionSql(context);
        string[] routines =
        [
            $"{read}(uuid, timestamp with time zone, integer)",
            $"{admit}(integer, timestamp with time zone)"
        ];
        IReadOnlyDictionary<string, IReadOnlyList<string>> nestedExecutors =
            new Dictionary<string, IReadOnlyList<string>>
            {
                [routines[0]] = [PostgresOutboxExecutionContract.OwnerRole]
            };
        string acl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(routines, OwnerRole, GlobalRole,
            requireComplete: true, additionalExecutors: nestedExecutors);
        var sources = new (string Table, string Columns)[]
        {
            (events, "id, tenant_id, event_status_id, is_deleted"),
            (reports, "id, tenant_id, event_id, is_deleted, created_at, provider_sync_outbox_id, provider_sync_case_id, provider_sync_case_concurrency_stamp, provider_sync_submitted_at_utc, provider_sync_payload_digest"),
            (cases, "id, tenant_id, report_id"),
            (correctionReceipts, "id, tenant_id, event_id, event_location_id, policy_version, created_at_utc, event_type, disclosure_audit_id, erasure_intent_id, authority_sequence, location_id"),
            (orders, "id, tenant_id, event_id, registration_order_status_id"),
            (delivery, "id, tenant_id, admission_ticket_id"),
            (recoveryRequest, "id, tenant_id"),
            (recoveryDelivery, "id, tenant_id, admission_ticket_id, recovery_request_id"),
            (campaigns, "id, tenant_id, event_id, kind"),
            (refunds, "id, tenant_id, payment_attempt_id, registration_order_id, paid_order_acceptance_snapshot_id"),
            (payments, "id, tenant_id, registration_order_id"),
            (manifests, "id, status, effect_outbox_id"),
            (imports, "id, status, target_tenant_id, effect_outbox_id, effect_payload_digest"),
            (provisioning, "id, managed_instance_id, current_outbox_message_id")
        };
        string grants = string.Join("\n", sources.Select(source => $"""
            REVOKE ALL ON TABLE {source.Table} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({source.Columns}) ON {source.Table} TO {OwnerRole};
            DROP POLICY IF EXISTS generic_outbox_admission ON {source.Table};
            CREATE POLICY generic_outbox_admission ON {source.Table} FOR SELECT TO {OwnerRole} USING (true);
            """));
        string relationIds = string.Join(", ", sources.Select(source => source.Table).Append(outbox).Append(locations).Append(audits)
            .Select(table => $"'{table.Replace("'", "''", StringComparison.Ordinal)}'::regclass"));
        string result = """
            "MessageId" uuid, "TenantId" uuid, "Ownership" integer, "SourceId" uuid,
            "SourceIdentity" text, "AggregateId" uuid, "AggregateType" text, "EventType" text,
            "PayloadDigest" text, "CreatedAt" timestamp with time zone,
            "Status" integer, "LeaseExpiresAt" timestamp with time zone
            """;
        string notificationTypes = """
            'EventPublishedNotificationFanoutRequested', 'EventLightModeratedNotificationFanoutRequested',
            'EventHeavyRedactedNotificationFanoutRequested', 'NotificationFanoutOccurrenceRequested',
            'EventDiscoveryIdentityCorrectionRequested'
            """;
        string unsupportedDeadLetter = "'EventReportProviderSyncRequested', 'RegistrationOrderRejected', 'RegistrationOrderConfirmed'";
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
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                    WHERE m.member IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{GlobalRole}')))
                    OR pg_has_role('{MigratorRole}', '{GlobalRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'generic outbox admission and migration authority must remain separate';
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
                    RAISE EXCEPTION 'generic outbox admission and migration logins must remain separate';
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid IN ({relationIds})
                    AND (c.relowner IN (SELECT oid FROM pg_catalog.pg_roles
                            WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))
                        OR has_table_privilege('{GlobalRole}', c.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR has_any_column_privilege('{GlobalRole}', c.oid, 'SELECT,INSERT,UPDATE,REFERENCES'))) THEN
                    RAISE EXCEPTION 'generic outbox admission role must not own or directly access source tables';
                END IF;
            END $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {outbox} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT (id, aggregate_id, aggregate_type, event_type, payload, status, next_retry_at, created_at)
                ON {outbox} TO {OwnerRole};
            GRANT UPDATE (status, next_retry_at, last_error) ON {outbox} TO {OwnerRole};
            REVOKE ALL ON TABLE {locations}, {audits} FROM {OwnerRole}, {GlobalRole};
            DROP POLICY IF EXISTS generic_outbox_admission ON {locations};
            DROP POLICY IF EXISTS generic_outbox_admission ON {audits};
            {grants}
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {read}(p_id uuid, p_lease timestamp with time zone, p_status integer)
                RETURNS TABLE ({result})
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE r record; v_tenant uuid; v_source uuid; v_identity text; v_ownership integer := 1;
                    v_other uuid; v_order uuid; v_event uuid;
                    v_posted uuid; v_submission_version uuid; v_submission_digest text;
                    v_location uuid; v_sequence bigint; v_policy integer;
                BEGIN
                    {acl}
                    IF p_id IS NULL OR p_lease IS NULL OR NOT isfinite(p_lease)
                        OR p_lease <= statement_timestamp() OR p_status IS NULL
                        OR p_status NOT IN ({(int)OutboxMessageStatus.Processing}, {(int)OutboxMessageStatus.DeadLettered})
                        THEN RETURN; END IF;
                    SELECT id, aggregate_id, aggregate_type, event_type, payload,
                        created_at, status, next_retry_at INTO r
                    FROM {outbox} WHERE id = p_id AND status = p_status AND next_retry_at = p_lease;
                    IF NOT FOUND OR r.aggregate_id = '00000000-0000-0000-0000-000000000000'::uuid
                        OR (p_status = {(int)OutboxMessageStatus.DeadLettered} AND r.event_type IN ({unsupportedDeadLetter}))
                        THEN RETURN; END IF;
                    IF r.event_type = 'ConfigurationImportEffectsRequested' THEN
                        IF r.payload IS NOT NULL AND jsonb_typeof(r.payload::jsonb) <> 'array' THEN RETURN; END IF;
                    ELSE
                        IF r.payload IS NOT NULL AND jsonb_typeof(r.payload::jsonb) <> 'object' THEN RETURN; END IF;
                        IF EXISTS (SELECT 1 FROM json_each(r.payload::json) GROUP BY lower(key) HAVING count(*) > 1) THEN RETURN; END IF;
                    END IF;
                    v_source := r.aggregate_id; v_identity := r.aggregate_id::text;
                    v_posted := (r.payload::jsonb ->> 'TenantId')::uuid;
                    CASE r.event_type
                    WHEN 'PrivacyErasureCacheInvalidationRequested' THEN
                        IF r.aggregate_type <> 'User' OR r.payload IS NOT NULL THEN RETURN; END IF;
                        v_ownership := 2;
                    WHEN 'LocationPiiErased' THEN
                        IF r.aggregate_type <> 'Location' OR (r.payload::jsonb ->> 'LocationId')::uuid IS DISTINCT FROM r.aggregate_id
                            OR NULLIF(r.payload::jsonb ->> 'IntentId', '00000000-0000-0000-0000-000000000000') IS NULL THEN RETURN; END IF;
                        v_ownership := 2; v_identity := v_identity || ':' || (r.payload::jsonb ->> 'IntentId')::uuid::text;
                    WHEN 'OperatorIdentityImported' THEN
                        IF r.aggregate_type <> 'InstanceOperatorIdentity' OR r.payload IS NULL THEN RETURN; END IF;
                        v_ownership := 3;
                    WHEN 'EventReportProviderSyncRequested' THEN
                        IF r.aggregate_type <> 'EventReport' OR (r.payload::jsonb ->> 'ReportId')::uuid IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT report.tenant_id, report.event_id, c.id,
                            report.provider_sync_case_concurrency_stamp, report.provider_sync_payload_digest
                            INTO v_tenant, v_event, v_other, v_submission_version, v_submission_digest
                        FROM {reports} report INNER JOIN {cases} c ON c.report_id = report.id AND c.tenant_id = report.tenant_id
                        WHERE report.id = r.aggregate_id AND NOT report.is_deleted
                            AND report.provider_sync_outbox_id = r.id
                            AND c.id = report.provider_sync_case_id AND c.id = (r.payload::jsonb ->> 'CaseId')::uuid
                            AND report.provider_sync_case_concurrency_stamp <> '00000000-0000-0000-0000-000000000000'::uuid
                            AND report.provider_sync_case_concurrency_stamp = (r.payload::jsonb ->> 'CaseConcurrencyStamp')::uuid
                            AND report.provider_sync_submitted_at_utc = report.created_at
                            AND report.provider_sync_submitted_at_utc = r.created_at
                            AND right(r.payload::jsonb ->> 'SubmittedAtUtc', 1) = 'Z'
                            AND report.provider_sync_submitted_at_utc = (r.payload::jsonb ->> 'SubmittedAtUtc')::timestamp with time zone
                            AND length(report.provider_sync_payload_digest) = 64
                            AND report.provider_sync_payload_digest !~ '[^0-9A-F]'
                            AND report.provider_sync_payload_digest =
                                upper(encode(sha256(convert_to(r.payload, 'UTF8')), 'hex'));
                        IF v_event IS DISTINCT FROM (r.payload::jsonb ->> 'EventId')::uuid THEN RETURN; END IF;
                        v_identity := v_identity || ':' || v_event::text || ':' || v_other::text
                            || ':' || v_submission_version::text || ':' || v_submission_digest;
                    WHEN 'LocationPrivacyCorrectionRequested', 'location.privacy.corrected' THEN
                        IF r.aggregate_type <> 'EventLocation' OR (r.payload::jsonb ->> 'EventLocationId')::uuid IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT id, tenant_id, event_id, disclosure_audit_id, erasure_intent_id,
                            authority_sequence, location_id, policy_version
                            INTO v_source, v_tenant, v_event, v_other, v_order, v_sequence, v_location, v_policy
                        FROM {correctionReceipts}
                        WHERE id = r.id AND event_location_id = r.aggregate_id
                            AND event_type = r.event_type AND created_at_utc = r.created_at;
                        IF NOT FOUND OR v_policy <= 0
                            OR v_policy IS DISTINCT FROM (r.payload::jsonb ->> 'PolicyVersion')::integer
                            OR (r.payload::jsonb ->> 'SchemaVersion')::integer IS DISTINCT FROM 1
                            OR v_event IS DISTINCT FROM (r.payload::jsonb ->> 'EventId')::uuid THEN RETURN; END IF;
                        v_identity := v_source::text || ':' || r.aggregate_id::text || ':' || v_event::text;
                        IF r.event_type = 'LocationPrivacyCorrectionRequested' THEN
                            IF v_other IS NOT NULL OR v_order IS NULL
                                OR v_order = '00000000-0000-0000-0000-000000000000'::uuid
                                OR v_order IS DISTINCT FROM (r.payload::jsonb ->> 'IntentId')::uuid
                                OR v_location IS NULL OR v_location = '00000000-0000-0000-0000-000000000000'::uuid
                                OR v_location IS DISTINCT FROM (r.payload::jsonb ->> 'LocationId')::uuid
                                OR v_sequence IS NULL OR v_sequence <= 0
                                OR v_sequence IS DISTINCT FROM (r.payload::jsonb ->> 'AuthoritySequence')::bigint
                                OR (r.payload::jsonb ->> 'SchemaVersion')::integer IS DISTINCT FROM 1 THEN RETURN; END IF;
                            v_identity := v_identity || ':' || v_order::text || ':' || v_location::text || ':' || v_sequence::text;
                        ELSE
                            IF v_other IS NULL OR v_other = '00000000-0000-0000-0000-000000000000'::uuid
                                OR v_order IS NOT NULL OR v_sequence IS NOT NULL OR v_location IS NOT NULL THEN RETURN; END IF;
                            v_identity := v_identity || ':' || v_other::text;
                        END IF;
                        v_identity := v_identity || ':' || v_policy::text;
                    WHEN 'RegistrationOrderRejected', 'RegistrationOrderConfirmed', 'RegistrationOrderCancelled' THEN
                        IF r.aggregate_type <> 'RegistrationOrder' OR (r.payload::jsonb ->> 'RegistrationOrderId')::uuid IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT tenant_id, event_id INTO v_tenant, v_event FROM {orders}
                        WHERE id = r.aggregate_id AND (r.event_type <> 'RegistrationOrderCancelled'
                            OR registration_order_status_id = {(int)RegistrationOrderStatusEnum.Cancelled});
                        IF v_event IS DISTINCT FROM (r.payload::jsonb ->> 'EventId')::uuid THEN RETURN; END IF;
                        v_identity := v_identity || ':' || v_event::text;
                    WHEN 'admission.event_cancellation.requested' THEN
                        IF r.aggregate_type <> 'AdmissionTicket' OR (r.payload::jsonb ->> 'EventId')::uuid IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT tenant_id INTO v_tenant FROM {events}
                            WHERE id = r.aggregate_id AND NOT is_deleted AND event_status_id = {(int)EventStatusEnum.Cancelled};
                    WHEN 'AdmissionCredentialDeliveryRequested' THEN
                        IF r.aggregate_type <> 'AdmissionTicket' OR (r.payload::jsonb ->> 'DeliveryIntentId')::uuid IS DISTINCT FROM r.id
                            OR (r.payload::jsonb ->> 'AdmissionTicketId')::uuid IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT tenant_id INTO v_tenant FROM {delivery} WHERE id = r.id AND admission_ticket_id = r.aggregate_id;
                        v_source := r.id; v_identity := r.id::text || ':' || r.aggregate_id::text;
                    WHEN 'AdmissionRecoveryRequestProcessingRequested' THEN
                        IF r.aggregate_type <> 'AdmissionRecoveryRequestIntent' OR r.aggregate_id <> r.id
                            OR (r.payload::jsonb ->> 'RequestIntentId')::uuid IS DISTINCT FROM r.id THEN RETURN; END IF;
                        SELECT tenant_id INTO v_tenant FROM {recoveryRequest} WHERE id = r.id;
                    WHEN 'AdmissionRecoveryDeliveryRequested' THEN
                        IF r.aggregate_type <> 'AdmissionRecoveryCapability' OR (r.payload::jsonb ->> 'DeliveryIntentId')::uuid IS DISTINCT FROM r.id
                            OR (r.payload::jsonb ->> 'AdmissionTicketId')::uuid IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT tenant_id, recovery_request_id INTO v_tenant, v_other FROM {recoveryDelivery}
                            WHERE id = r.id AND admission_ticket_id = r.aggregate_id;
                        v_source := r.id; v_identity := r.id::text || ':' || r.aggregate_id::text || ':' || v_other::text;
                    WHEN 'registration.refund_campaign.process_requested' THEN
                        IF r.aggregate_type <> 'RefundCampaign' OR (r.payload::jsonb ->> 'CampaignId')::uuid IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT tenant_id, event_id INTO v_tenant, v_event FROM {campaigns} WHERE id = r.aggregate_id;
                        v_identity := v_identity || ':' || v_event::text;
                    WHEN 'registration.refund.dispatch_requested', 'registration.refund.reconciliation_requested' THEN
                        IF r.aggregate_type <> 'RefundAttempt' OR (r.payload::jsonb ->> 'RefundAttemptId')::uuid IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT tenant_id, payment_attempt_id, registration_order_id, paid_order_acceptance_snapshot_id
                            INTO v_tenant, v_other, v_order, v_event FROM {refunds} WHERE id = r.aggregate_id;
                        v_identity := v_identity || ':' || v_other::text || ':' || v_order::text || ':' || v_event::text;
                    WHEN 'registration.payment.cancellation_requested' THEN
                        IF r.aggregate_type <> 'PaymentAttempt' OR (r.payload::jsonb ->> 'PaymentAttemptId')::uuid IS DISTINCT FROM r.aggregate_id THEN RETURN; END IF;
                        SELECT p.tenant_id, o.id, c.id, o.event_id INTO v_tenant, v_order, v_other, v_event
                        FROM {payments} p INNER JOIN {orders} o ON o.id = p.registration_order_id AND o.tenant_id = p.tenant_id
                        INNER JOIN {campaigns} c ON c.event_id = o.event_id AND c.tenant_id = o.tenant_id
                        WHERE p.id = r.aggregate_id AND c.id = (r.payload::jsonb ->> 'CampaignId')::uuid
                            AND c.kind = {(int)RefundCampaignKind.EventCancellation};
                        v_identity := v_identity || ':' || v_order::text || ':' || v_other::text || ':' || v_event::text;
                    WHEN 'ConfigurationManifestEffectsRequested' THEN
                        IF r.aggregate_type <> 'ConfigurationManifestOperation' OR r.payload IS NULL
                            OR NOT EXISTS (SELECT 1 FROM {manifests}
                                WHERE id = r.aggregate_id AND status = 'Applied' AND effect_outbox_id = r.id) THEN RETURN; END IF;
                        v_ownership := 3; v_identity := v_identity || ':' || r.id::text;
                    WHEN 'ConfigurationImportEffectsRequested' THEN
                        IF r.aggregate_type <> 'ConfigurationImportOperation' THEN RETURN; END IF;
                        SELECT target_tenant_id, effect_payload_digest INTO v_tenant, v_submission_digest FROM {imports}
                            WHERE id = r.aggregate_id AND effect_outbox_id = r.id
                            AND status IN ({(int)ConfigurationImportOperationStatus.Applied}, {(int)ConfigurationImportOperationStatus.RolledBack})
                            AND length(effect_payload_digest) = 64 AND effect_payload_digest !~ '[^0-9A-F]'
                            AND effect_payload_digest = upper(encode(sha256(convert_to(COALESCE(r.payload, ''), 'UTF8')), 'hex'));
                        IF NOT FOUND THEN RETURN; END IF;
                        v_ownership := CASE WHEN v_tenant IS NULL THEN 3 ELSE 1 END;
                        v_posted := v_tenant;
                        v_identity := v_identity || ':' || r.id::text || ':' || v_submission_digest;
                    WHEN 'ManagedTenantProvisioningProcessRequested' THEN
                        IF r.aggregate_type <> 'ManagedTenantProvisioningOperation' OR r.payload IS NOT NULL THEN RETURN; END IF;
                        SELECT managed_instance_id INTO v_other FROM {provisioning}
                            WHERE id = r.aggregate_id AND current_outbox_message_id = r.id;
                        IF NOT FOUND THEN RETURN; END IF;
                        v_ownership := 3; v_identity := v_identity || ':' || v_other::text || ':' || r.id::text;
                    ELSE RETURN;
                    END CASE;
                    IF v_identity IS NULL OR (v_ownership = 1 AND
                        (v_tenant IS NULL OR v_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                            OR v_posted IS DISTINCT FROM v_tenant)) THEN RETURN; END IF;
                    RETURN QUERY SELECT r.id, v_tenant, v_ownership, v_source, v_identity, r.aggregate_id,
                        r.aggregate_type::text, r.event_type::text,
                        encode(sha256(convert_to(CASE WHEN r.payload IS NULL THEN '0' ELSE '1' || r.payload END, 'UTF8')), 'hex'),
                        r.created_at, r.status, r.next_retry_at;
                EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range
                    OR invalid_datetime_format OR datetime_field_overflow THEN RETURN;
                END $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {admit}(p_take integer, p_claimed timestamp with time zone)
                RETURNS TABLE ({result})
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                DECLARE r record; admitted record; v_status integer;
                    v_now timestamp with time zone := LEAST(p_claimed, statement_timestamp());
                    v_lease timestamp with time zone;
                BEGIN
                    {acl}
                    IF p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000 OR p_claimed IS NULL OR NOT isfinite(p_claimed) THEN
                        RAISE EXCEPTION 'invalid generic outbox admission bounds' USING ERRCODE = '22023';
                    END IF;
                    v_lease := v_now + interval '5 minutes';
                    FOR r IN SELECT id, status FROM {outbox}
                        WHERE event_type NOT IN ({notificationTypes})
                        AND ((status = {(int)OutboxMessageStatus.Pending} AND (next_retry_at IS NULL OR next_retry_at <= v_now))
                            OR (status = {(int)OutboxMessageStatus.Processing} AND next_retry_at <= v_now)
                            OR (status = {(int)OutboxMessageStatus.DeadLettered} AND next_retry_at <= v_now
                                AND event_type NOT IN ({unsupportedDeadLetter})))
                        ORDER BY created_at, id LIMIT p_take FOR UPDATE SKIP LOCKED
                    LOOP
                        v_status := CASE WHEN r.status = {(int)OutboxMessageStatus.DeadLettered}
                            THEN {(int)OutboxMessageStatus.DeadLettered} ELSE {(int)OutboxMessageStatus.Processing} END;
                        UPDATE {outbox} SET status = v_status, next_retry_at = v_lease WHERE id = r.id;
                        SELECT * INTO admitted FROM {read}(r.id, v_lease, v_status);
                        IF FOUND THEN
                            RETURN QUERY SELECT admitted."MessageId", admitted."TenantId", admitted."Ownership",
                                admitted."SourceId", admitted."SourceIdentity", admitted."AggregateId",
                                admitted."AggregateType", admitted."EventType", admitted."PayloadDigest",
                                admitted."CreatedAt", admitted."Status", admitted."LeaseExpiresAt";
                        ELSE UPDATE {outbox} SET status = CASE WHEN v_status = {(int)OutboxMessageStatus.DeadLettered}
                            THEN v_status ELSE {(int)OutboxMessageStatus.Failed} END,
                            last_error = 'generic_source_not_admitted'
                            WHERE id = r.id AND status = v_status AND next_retry_at = v_lease;
                        END IF;
                    END LOOP;
                END $function$ $ddl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE ALL ON FUNCTION {read}(uuid, timestamp with time zone, integer) FROM PUBLIC;
            REVOKE ALL ON FUNCTION {admit}(integer, timestamp with time zone) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {read}(uuid, timestamp with time zone, integer) TO {GlobalRole};
            GRANT EXECUTE ON FUNCTION {admit}(integer, timestamp with time zone) TO {GlobalRole};
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $acl_exit$ BEGIN {acl} END $acl_exit$;
            """;
    }
}
