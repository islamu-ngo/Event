using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Security;

public static partial class PostgresLocationPrivacyGovernanceContract
{
    private static string BuildReadSql(Names names)
    {
        string setting = names.Table<TenantSetting>();
        string tenant = names.Column<TenantSetting>(nameof(TenantSetting.TenantId));
        string key = names.Column<TenantSetting>(nameof(TenantSetting.SettingKey));
        string value = names.Column<TenantSetting>(nameof(TenantSetting.Value));
        string id = names.Column<TenantSetting>(nameof(TenantSetting.Id));
        string projectionTenant = names.Column<EventLocation>(nameof(EventLocation.TenantId));
        string eventId = names.Column<EventLocation>(nameof(EventLocation.EventId));
        string projectionId = names.Column<EventLocation>(nameof(EventLocation.Id));
        string deleted = names.Column<EventLocation>(nameof(EventLocation.IsDeleted));
        return $$"""
            CREATE OR REPLACE FUNCTION {{names.Function("overrides")}}(p_key text, p_tenant uuid)
            RETURNS TABLE ("TenantId" uuid, "StoredValue" text)
            LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            BEGIN
                {{ScopeAuthoritySql()}}
                IF p_key IS NULL OR p_key NOT IN ({{names.KeysSql}})
                    OR (p_tenant IS NOT NULL AND NOT ({{UuidSql("p_tenant")}})) THEN
                    RAISE EXCEPTION 'location_governance_scope_invalid';
                END IF;
                RETURN QUERY
                    SELECT setting.{{tenant}}, setting.{{value}}::text
                    FROM {{setting}} setting
                    WHERE setting.{{key}} = p_key AND (p_tenant IS NULL OR setting.{{tenant}} = p_tenant)
                    ORDER BY setting.{{tenant}}, setting.{{id}};
            END; $body$;
            CREATE OR REPLACE FUNCTION {{names.Function("projections")}}(p_tenant uuid)
            RETURNS TABLE (
                "TenantId" uuid, "EventId" uuid, "EventLocationId" uuid, "IsDeleted" boolean,
                "IsToBeAnnounced" boolean, "LocationKindId" integer, "Fields" integer,
                "Audience" integer, "RevealFullDetailsFromUtc" timestamp with time zone,
                "CreatedAt" timestamp with time zone, "PolicyVersion" integer, "ConcurrencyStamp" uuid)
            LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            BEGIN
                {{ScopeAuthoritySql()}}
                IF p_tenant IS NOT NULL AND NOT ({{UuidSql("p_tenant")}}) THEN
                    RAISE EXCEPTION 'location_governance_scope_invalid';
                END IF;
                RETURN QUERY
                    SELECT {{SnapshotColumns(names)}}
                    {{SnapshotFrom(names)}}
                    WHERE NOT projection.{{deleted}}
                        AND (p_tenant IS NULL OR projection.{{projectionTenant}} = p_tenant)
                    ORDER BY projection.{{projectionTenant}}, projection.{{eventId}}, projection.{{projectionId}};
            END; $body$;
            """;
    }

    private static string SnapshotFrom(Names names) => $"""
        FROM {names.Table<EventLocation>()} projection
        LEFT JOIN {names.Table<Location>()} location
            ON location.{names.Column<Location>(nameof(Location.TenantId))} =
                projection.{names.Column<EventLocation>(nameof(EventLocation.TenantId))}
            AND location.{names.Column<Location>(nameof(Location.Id))} =
                projection.{names.Column<EventLocation>(nameof(EventLocation.LocationId))}
        """;

    private static string SnapshotColumns(Names names)
    {
        string P(string property) => $"projection.{names.Column<EventLocation>(property)}";
        return $"""
            {P(nameof(EventLocation.TenantId))} AS tenant_id,
            {P(nameof(EventLocation.EventId))} AS event_id,
            {P(nameof(EventLocation.Id))} AS event_location_id,
            {P(nameof(EventLocation.IsDeleted))} AS is_deleted,
            {P(nameof(EventLocation.IsToBeAnnounced))} AS is_to_be_announced,
            location.{names.Column<Location>(nameof(Location.LocationKindId))} AS location_kind_id,
            {FieldsSql(names)} AS fields,
            {P(nameof(EventLocation.FullDetailsAudienceId))} AS audience,
            {P(nameof(EventLocation.RevealFullDetailsFromUtc))} AS reveal_at,
            {P(nameof(EventLocation.CreatedAt))} AS created_at,
            {P(nameof(EventLocation.PolicyVersion))} AS policy_version,
            {P(nameof(EventLocation.ConcurrencyStamp))} AS stamp
            """;
    }

    private static string FieldsSql(Names names)
    {
        (string Property, EventLocationDisclosureFields Flag)[] fields =
        [
            (nameof(EventLocation.ShowVenueName), EventLocationDisclosureFields.VenueName),
            (nameof(EventLocation.ShowCity), EventLocationDisclosureFields.City),
            (nameof(EventLocation.ShowCountry), EventLocationDisclosureFields.Country),
            (nameof(EventLocation.ShowRoomName), EventLocationDisclosureFields.RoomName),
            (nameof(EventLocation.ShowStreetAddress), EventLocationDisclosureFields.StreetAddress),
            (nameof(EventLocation.ShowPostcode), EventLocationDisclosureFields.Postcode),
            (nameof(EventLocation.ShowCoordinates), EventLocationDisclosureFields.Coordinates)
        ];
        return "(" + string.Join(" | ", fields.Select(field =>
            $"CASE WHEN projection.{names.Column<EventLocation>(field.Property)} THEN {(int)field.Flag} ELSE 0 END")) + ")";
    }

    private static string UuidSql(string expression) =>
        $"({expression} IS NOT NULL AND substring(({expression})::text, 15, 1) = '7' " +
        $"AND substring(({expression})::text, 20, 1) IN ('8','9','a','b'))";

    private static string ScopeAuthoritySql() => $"""
        IF NOT pg_has_role(session_user, '{GlobalRole}', 'USAGE')
            AND (p_tenant IS NULL OR p_tenant IS DISTINCT FROM
                nullif(current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid) THEN
            RAISE EXCEPTION 'location_governance_scope_authority_invalid' USING ERRCODE = '42501';
        END IF;
        """;

    private static string TimeSql(string expression) =>
        $"({expression} IS NOT NULL AND isfinite({expression}) " +
        $"AND {expression} > TIMESTAMPTZ '0001-01-01 00:00:00+00' " +
        $"AND {expression} < TIMESTAMPTZ '9999-12-31 23:59:59.999999+00')";
}
