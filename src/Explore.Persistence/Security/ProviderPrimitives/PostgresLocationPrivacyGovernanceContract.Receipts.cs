using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Privacy.ErasureAuthority;

namespace Explore.Persistence.Security;

public static partial class PostgresLocationPrivacyGovernanceContract
{
    private static string GovernanceReceiptInsertSql(
        Names names, string message, string tenant, string sourceEvent, string placement,
        string version, string created, string audit) => $"""
        INSERT INTO {names.Table<LocationPrivacyCorrectionReceipt>()}
            ({names.Columns<LocationPrivacyCorrectionReceipt>(
                nameof(LocationPrivacyCorrectionReceipt.Id), nameof(LocationPrivacyCorrectionReceipt.TenantId),
                nameof(LocationPrivacyCorrectionReceipt.EventId), nameof(LocationPrivacyCorrectionReceipt.EventLocationId),
                nameof(LocationPrivacyCorrectionReceipt.PolicyVersion), nameof(LocationPrivacyCorrectionReceipt.CreatedAtUtc),
                nameof(LocationPrivacyCorrectionReceipt.EventType), nameof(LocationPrivacyCorrectionReceipt.DisclosureAuditId))})
        VALUES ({message}, {tenant}, {sourceEvent}, {placement}, {version}, {created},
            'location.privacy.corrected', {audit});
        """;

    private static string BuildReceiptSql(Names names)
    {
        string A(string field) => $"audit.{names.Column<EventLocationDisclosureAudit>(field)}";
        string P(string field) => $"projection.{names.Column<EventLocation>(field)}";
        string M(string field) => $"message.{names.Column<OutboxMessage>(field)}";
        return $$"""
            CREATE OR REPLACE FUNCTION {{names.Function("retain_receipt")}}(
                p_tenant uuid, p_message uuid, p_audit uuid) RETURNS void
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body$
            DECLARE source record;
            BEGIN
                {{ScopeAuthoritySql()}}
                IF p_tenant IS NULL OR NOT ({{UuidSql("p_message")}}) OR NOT ({{UuidSql("p_audit")}}) THEN
                    RAISE EXCEPTION 'location_correction_receipt_source_missing' USING ERRCODE = '42501';
                END IF;
                SELECT {{P(nameof(EventLocation.EventId))}} AS event_id,
                    {{P(nameof(EventLocation.Id))}} AS placement_id,
                    {{A(nameof(EventLocationDisclosureAudit.NewPolicyVersion))}} AS version,
                    {{M(nameof(OutboxMessage.CreatedAt))}} AS created_at
                INTO source
                FROM {{names.Table<EventLocationDisclosureAudit>()}} audit
                JOIN {{names.Table<EventLocation>()}} projection
                    ON {{P(nameof(EventLocation.TenantId))}} = {{A(nameof(EventLocationDisclosureAudit.TenantId))}}
                    AND {{P(nameof(EventLocation.Id))}} = {{A(nameof(EventLocationDisclosureAudit.EventLocationId))}}
                JOIN {{names.Table<OutboxMessage>()}} message
                    ON {{M(nameof(OutboxMessage.Id))}} = p_message
                    AND {{M(nameof(OutboxMessage.AggregateType))}} = 'EventLocation'
                    AND {{M(nameof(OutboxMessage.AggregateId))}} = {{P(nameof(EventLocation.Id))}}
                WHERE {{A(nameof(EventLocationDisclosureAudit.Id))}} = p_audit
                    AND {{A(nameof(EventLocationDisclosureAudit.CorrectionMessageId))}} = p_message
                    AND {{A(nameof(EventLocationDisclosureAudit.TenantId))}} = p_tenant
                    AND {{A(nameof(EventLocationDisclosureAudit.Reason))}} IN (
                        {{(int)EventLocationDisclosureAuditReasonEnum.GovernanceTightening}},
                        {{(int)EventLocationDisclosureAuditReasonEnum.PrivacyErasureRemediation}})
                    AND {{A(nameof(EventLocationDisclosureAudit.NewPolicyVersion))}} = {{P(nameof(EventLocation.PolicyVersion))}}
                    AND {{A(nameof(EventLocationDisclosureAudit.OccurredAtUtc))}} = {{M(nameof(OutboxMessage.CreatedAt))}}
                    AND {{M(nameof(OutboxMessage.Status))}} = {{(int)OutboxMessageStatus.Pending}}
                    AND {{M(nameof(OutboxMessage.EventType))}} = 'location.privacy.corrected'
                    AND {{M(nameof(OutboxMessage.Payload))}}::jsonb = jsonb_build_object(
                        'SchemaVersion', 1, 'TenantId', p_tenant,
                        'EventId', {{P(nameof(EventLocation.EventId))}},
                        'EventLocationId', {{P(nameof(EventLocation.Id))}},
                        'PolicyVersion', {{A(nameof(EventLocationDisclosureAudit.NewPolicyVersion))}})
                FOR UPDATE OF projection;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'location_correction_receipt_source_mismatch' USING ERRCODE = '42501';
                END IF;
                {{GovernanceReceiptInsertSql(names, "p_message", "p_tenant", "source.event_id",
                    "source.placement_id", "source.version", "source.created_at", "p_audit")}}
            END; $body$;
            CREATE OR REPLACE FUNCTION {{names.Function("receipt_custody")}}() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog AS $body$
            BEGIN
                IF TG_OP <> 'INSERT' OR current_user NOT IN (
                    '{{OwnerRole}}', '{{PostgresSubjectPrivacyErasureContract.OwnerRole}}') THEN
                    RAISE EXCEPTION 'location_correction_receipt_immutable_source_required' USING ERRCODE = '42501';
                END IF;
                RETURN NEW;
            END; $body$;
            REVOKE ALL ON FUNCTION {{names.Function("receipt_custody")}}() FROM PUBLIC;
            """;
    }
}
