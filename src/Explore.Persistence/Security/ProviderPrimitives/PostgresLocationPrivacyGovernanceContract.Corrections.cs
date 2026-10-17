using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresLocationPrivacyGovernanceContract
{
    private static string BuildCorrectionSql(Names names)
    {
        string C(string property) => names.Column<EventLocation>(property);
        string auditValues = string.Join(", ", names.Properties<EventLocationDisclosureAudit>().Select(property => property.Name switch
        {
            nameof(EventLocationDisclosureAudit.Id) => "(a->>'Id')::uuid",
            nameof(EventLocationDisclosureAudit.TenantId) => "v_tenant",
            nameof(EventLocationDisclosureAudit.EventLocationId) => "v_id",
            nameof(EventLocationDisclosureAudit.CorrectionMessageId) => "(m->>'Id')::uuid",
            nameof(EventLocationDisclosureAudit.ActorUserId) => "p_actor",
            nameof(EventLocationDisclosureAudit.PreviousFields) or nameof(EventLocationDisclosureAudit.NewFields) => "v_fields",
            nameof(EventLocationDisclosureAudit.PreviousAudienceId) or nameof(EventLocationDisclosureAudit.NewAudienceId) => "v_audience",
            nameof(EventLocationDisclosureAudit.PreviousRevealFullDetailsFromUtc)
                or nameof(EventLocationDisclosureAudit.NewRevealFullDetailsFromUtc) => "v_reveal",
            nameof(EventLocationDisclosureAudit.PreviousPolicyVersion) => "v_version",
            nameof(EventLocationDisclosureAudit.NewPolicyVersion) => "v_version + 1",
            nameof(EventLocationDisclosureAudit.Reason) => ((int)EventLocationDisclosureAuditReasonEnum.GovernanceTightening).ToString(System.Globalization.CultureInfo.InvariantCulture),
            nameof(EventLocationDisclosureAudit.OccurredAtUtc) => "p_changed",
            _ => throw new InvalidOperationException($"Unknown governance audit column {property.Name}.")
        }));
        var defaults = new OutboxMessage
        {
            AggregateType = nameof(EventLocation),
            EventType = "location.privacy.corrected",
            Status = OutboxMessageStatus.Pending,
            MaxRetries = 10
        };
        string payload = """
            jsonb_build_object('SchemaVersion', 1, 'TenantId', v_tenant, 'EventId', v_event,
                'EventLocationId', v_id, 'PolicyVersion', v_version + 1)
            """;
        string outboxValues = string.Join(", ", names.OutboxProperties.Select(property => property.Name switch
        {
            nameof(OutboxMessage.Id) => "(m->>'Id')::uuid",
            nameof(OutboxMessage.AggregateType) => "'EventLocation'",
            nameof(OutboxMessage.AggregateId) => "v_id",
            nameof(OutboxMessage.EventType) => "'location.privacy.corrected'",
            nameof(OutboxMessage.Payload) => $"({payload})::text",
            nameof(OutboxMessage.Status) => ((int)OutboxMessageStatus.Pending).ToString(System.Globalization.CultureInfo.InvariantCulture),
            nameof(OutboxMessage.CreatedAt) => "p_changed",
            nameof(OutboxMessage.MaxRetries) => "10",
            _ => property.GetRelationalTypeMapping().GenerateSqlLiteral(
                property.PropertyInfo?.GetValue(defaults)
                ?? (property.IsNullable ? null : throw new InvalidOperationException($"Unknown governance outbox default {property.Name}.")))
        }));
        string audiences = string.Join(", ", Enum.GetValues<LocationDisclosureAudienceEnum>().Select(value => (int)value));
        return $$"""
            CREATE OR REPLACE FUNCTION {{names.Function("correct")}}(
                p_key text, p_tenant uuid, p_actor uuid, p_changed timestamp with time zone, p_targets jsonb)
            RETURNS TABLE ("TenantId" uuid)
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            DECLARE
                target jsonb; b jsonb; a jsonb; m jsonb;
                v_tenant uuid; v_event uuid; v_id uuid; v_stamp uuid; v_new_stamp uuid;
                v_version integer; v_fields integer; v_audience integer; v_kind integer;
                v_tba boolean; v_created timestamp with time zone; v_reveal timestamp with time zone;
                current_projection record; affected integer;
                seen_targets uuid[] := ARRAY[]::uuid[];
                seen_audits uuid[] := ARRAY[]::uuid[];
                seen_messages uuid[] := ARRAY[]::uuid[];
            BEGIN
                {{ScopeAuthoritySql()}}
                IF p_key IS NULL OR p_key NOT IN ({{names.KeysSql}})
                    OR (p_tenant IS NOT NULL AND NOT ({{UuidSql("p_tenant")}}))
                    OR NOT ({{UuidSql("p_actor")}})
                    OR NOT ({{TimeSql("p_changed")}})
                    OR jsonb_typeof(p_targets) IS DISTINCT FROM 'array' THEN
                    RAISE EXCEPTION 'location_governance_scope_invalid';
                END IF;
                IF jsonb_array_length(p_targets) NOT BETWEEN 1 AND 500 THEN
                    RAISE EXCEPTION 'location_governance_batch_invalid';
                END IF;
                FOR target IN SELECT value FROM jsonb_array_elements(p_targets)
                    ORDER BY value->'Before'->>'TenantId', value->'Before'->>'EventId', value->'Before'->>'EventLocationId'
                LOOP
                    b := target->'Before'; a := target->'Audit'; m := target->'Message';
                    IF jsonb_typeof(b) IS DISTINCT FROM 'object'
                        OR jsonb_typeof(a) IS DISTINCT FROM 'object' OR jsonb_typeof(m) IS DISTINCT FROM 'object'
                        OR target IS DISTINCT FROM jsonb_build_object('Before', b, 'Audit', a, 'Message', m, 'Stamp', target->'Stamp') THEN
                        RAISE EXCEPTION 'location_governance_target_invalid';
                    END IF;
                    v_tenant := (b->>'TenantId')::uuid;
                    v_event := (b->>'EventId')::uuid;
                    v_id := (b->>'EventLocationId')::uuid;
                    v_stamp := (b->>'ConcurrencyStamp')::uuid;
                    v_new_stamp := (target->>'Stamp')::uuid;
                    v_version := (b->>'PolicyVersion')::integer;
                    v_fields := (b->>'Fields')::integer;
                    v_audience := (b->>'Audience')::integer;
                    v_kind := (b->>'LocationKindId')::integer;
                    v_tba := (b->>'IsToBeAnnounced')::boolean;
                    v_created := (b->>'CreatedAt')::timestamp with time zone;
                    v_reveal := (b->>'RevealFullDetailsFromUtc')::timestamp with time zone;
                    IF ({{UuidSql("v_tenant")}} AND {{UuidSql("v_event")}} AND {{UuidSql("v_id")}}
                        AND {{UuidSql("v_stamp")}} AND {{UuidSql("v_new_stamp")}}
                        AND {{UuidSql("(a->>'Id')::uuid")}} AND {{UuidSql("(m->>'Id')::uuid")}}
                        AND v_stamp <> v_new_stamp AND (p_tenant IS NULL OR v_tenant = p_tenant)
                        AND v_version BETWEEN 1 AND 2147483646
                        AND v_fields BETWEEN 0 AND {{(int)EventLocationDisclosureFields.All}}
                        AND v_audience IN ({{audiences}}) AND v_tba IS NOT NULL
                        AND {{TimeSql("v_created")}}
                        AND (v_reveal IS NULL OR {{TimeSql("v_reveal")}})
                        AND b->>'CreatedAt' ~ '(Z|[+]00:00)$'
                        AND (v_reveal IS NULL OR b->>'RevealFullDetailsFromUtc' ~ '(Z|[+]00:00)$')
                        AND (a->>'OccurredAtUtc')::timestamp with time zone = p_changed
                        AND a->>'OccurredAtUtc' ~ '(Z|[+]00:00)$'
                        AND (m->>'CreatedAt')::timestamp with time zone = p_changed
                        AND m->>'CreatedAt' ~ '(Z|[+]00:00)$') IS NOT TRUE THEN
                        RAISE EXCEPTION 'location_governance_effect_invalid';
                    END IF;
                    IF b IS DISTINCT FROM jsonb_build_object(
                        'TenantId', v_tenant, 'EventId', v_event, 'EventLocationId', v_id,
                        'IsDeleted', false, 'IsToBeAnnounced', v_tba, 'LocationKindId', v_kind,
                        'Fields', v_fields, 'Audience', v_audience,
                        'RevealFullDetailsFromUtc', b->'RevealFullDetailsFromUtc', 'CreatedAt', b->'CreatedAt',
                        'PolicyVersion', v_version, 'ConcurrencyStamp', v_stamp)
                        OR a IS DISTINCT FROM jsonb_build_object(
                            'Id', (a->>'Id')::uuid, 'TenantId', v_tenant, 'EventLocationId', v_id,
                            'CorrectionMessageId', (m->>'Id')::uuid, 'ActorUserId', p_actor,
                            'PreviousFields', v_fields, 'NewFields', v_fields,
                            'PreviousAudienceId', v_audience, 'NewAudienceId', v_audience,
                            'PreviousRevealFullDetailsFromUtc', b->'RevealFullDetailsFromUtc',
                            'NewRevealFullDetailsFromUtc', b->'RevealFullDetailsFromUtc',
                            'PreviousPolicyVersion', v_version, 'NewPolicyVersion', v_version + 1,
                            'Reason', {{(int)EventLocationDisclosureAuditReasonEnum.GovernanceTightening}},
                            'OccurredAtUtc', a->'OccurredAtUtc')
                        OR m IS DISTINCT FROM jsonb_build_object(
                            'Id', (m->>'Id')::uuid, 'AggregateType', 'EventLocation', 'AggregateId', v_id,
                            'EventType', 'location.privacy.corrected', 'Payload', {{payload}},
                            'Status', {{(int)OutboxMessageStatus.Pending}}, 'CreatedAt', m->'CreatedAt', 'MaxRetries', 10)
                        OR v_id = ANY(seen_targets) OR (a->>'Id')::uuid = ANY(seen_audits)
                        OR (m->>'Id')::uuid = ANY(seen_messages) THEN
                        RAISE EXCEPTION 'location_governance_effect_invalid';
                    END IF;
                    seen_targets := array_append(seen_targets, v_id);
                    seen_audits := array_append(seen_audits, (a->>'Id')::uuid);
                    seen_messages := array_append(seen_messages, (m->>'Id')::uuid);

                    SELECT {{SnapshotColumns(names)}} INTO current_projection
                    {{SnapshotFrom(names)}}
                    WHERE projection.{{C(nameof(EventLocation.TenantId))}} = v_tenant
                        AND projection.{{C(nameof(EventLocation.EventId))}} = v_event
                        AND projection.{{C(nameof(EventLocation.Id))}} = v_id
                        AND NOT projection.{{C(nameof(EventLocation.IsDeleted))}}
                    FOR UPDATE OF projection;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'location_governance_target_missing';
                    END IF;
                    IF current_projection.policy_version IS DISTINCT FROM v_version
                        OR current_projection.stamp IS DISTINCT FROM v_stamp
                        OR current_projection.fields IS DISTINCT FROM v_fields
                        OR current_projection.audience IS DISTINCT FROM v_audience
                        OR current_projection.reveal_at IS DISTINCT FROM v_reveal
                        OR current_projection.created_at IS DISTINCT FROM v_created
                        OR current_projection.is_to_be_announced IS DISTINCT FROM v_tba
                        OR current_projection.location_kind_id IS DISTINCT FROM v_kind THEN
                        RAISE EXCEPTION 'location_governance_target_stale';
                    END IF;
                    UPDATE {{names.Table<EventLocation>()}} projection SET
                        {{C(nameof(EventLocation.NeedsPrivacyReview))}} = true,
                        {{C(nameof(EventLocation.PolicyVersion))}} = v_version + 1,
                        {{C(nameof(EventLocation.LastPolicyActorUserId))}} = p_actor,
                        {{C(nameof(EventLocation.LastPolicyChangedAtUtc))}} = p_changed,
                        {{C(nameof(EventLocation.UpdatedAt))}} = p_changed,
                        {{C(nameof(EventLocation.UpdatedBy))}} = p_actor,
                        {{C(nameof(EventLocation.ConcurrencyStamp))}} = v_new_stamp
                    WHERE projection.{{C(nameof(EventLocation.TenantId))}} = v_tenant
                        AND projection.{{C(nameof(EventLocation.EventId))}} = v_event
                        AND projection.{{C(nameof(EventLocation.Id))}} = v_id
                        AND projection.{{C(nameof(EventLocation.PolicyVersion))}} = v_version
                        AND projection.{{C(nameof(EventLocation.ConcurrencyStamp))}} = v_stamp
                        AND NOT projection.{{C(nameof(EventLocation.IsDeleted))}}
                        AND (SELECT location.{{names.Column<Location>(nameof(Location.LocationKindId))}}
                            FROM {{names.Table<Location>()}} location
                            WHERE location.{{names.Column<Location>(nameof(Location.TenantId))}} = v_tenant
                                AND location.{{names.Column<Location>(nameof(Location.Id))}} =
                                    projection.{{C(nameof(EventLocation.LocationId))}}) IS NOT DISTINCT FROM v_kind;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN
                        RAISE EXCEPTION 'location_governance_target_stale';
                    END IF;
                    INSERT INTO {{names.Table<EventLocationDisclosureAudit>()}} ({{names.AllColumns<EventLocationDisclosureAudit>()}})
                        VALUES ({{auditValues}});
                    INSERT INTO {{names.Table<OutboxMessage>()}} ({{names.OutboxColumns}})
                        VALUES ({{outboxValues}});
                    {{GovernanceReceiptInsertSql(names, "(m->>'Id')::uuid", "v_tenant", "v_event",
                        "v_id", "v_version + 1", "p_changed", "(a->>'Id')::uuid")}}
                    RETURN QUERY SELECT v_tenant;
                END LOOP;
            END; $body$;
            """;
    }
}
