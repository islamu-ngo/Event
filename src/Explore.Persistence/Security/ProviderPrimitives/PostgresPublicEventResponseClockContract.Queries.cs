using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventResponseClockContract
{
    private static string Queries(SourceSql sql)
    {
        string S(string property) => sql.Column<EventSession>("s", property);
        string V(string property) => sql.Column<AtprotoEventProjection>("v", property);
        string L(string property) => sql.Column<EventLocation>("l", property);
        string eligible = EligibleLocal(sql, "e");
        string sessions = $"""
            FROM {sql.Table<EventSession>()} s
            WHERE {sql.Live<EventSession>("s")}
                AND EXISTS (SELECT 1 FROM {sql.Table<Event>()} e WHERE {eligible}
                    AND {sql.Column<Event>("e", "Id")} = {S("EventId")})
                AND {S("EventSessionStatusId")} = {(int)EventSessionStatusEnum.Published}
                AND ({S("EventDayId")} IS NULL OR EXISTS (SELECT 1 FROM {sql.Table<EventDay>()} d
                    WHERE {sql.Live<EventDay>("d")} AND {sql.Column<EventDay>("d", "Id")} = {S("EventDayId")}
                        AND {sql.Column<EventDay>("d", "IsPublished")}))
            """;
        string locations = $"""
            FROM {sql.Table<EventLocation>()} l
            WHERE {sql.Live<EventLocation>("l")}
                AND EXISTS (SELECT 1 FROM {sql.Table<Event>()} e WHERE {eligible}
                    AND {sql.Column<Event>("e", "Id")} = {L("EventId")})
                AND NOT {L("IsToBeAnnounced")} AND NOT {L("NeedsPrivacyReview")}
                AND EXISTS (SELECT 1 FROM {sql.Table<Location>()} p
                    WHERE {sql.Live<Location>("p")} AND {sql.Column<Location>("p", "Id")} = {L("LocationId")}
                        AND {sql.Column<Location>("p", "LocationKindId")} <> {(int)LocationKindEnum.PrivateHome}
                        AND {sql.Column<Location>("p", "LocationPrivacyStateId")} = {(int)LocationPrivacyStateEnum.Active}
                        AND EXISTS (SELECT 1 FROM {sql.Table<LocationPii>()} pii
                            WHERE {sql.Column<LocationPii>("pii", "LocationId")} = {sql.Column<Location>("p", "Id")}))
            """;
        string disclosure = $"""
            AND (p_address AND ({L("ShowStreetAddress")} OR {L("ShowPostcode")})
                OR p_coordinates AND {L("ShowCoordinates")})
            """;
        return $"""
            CASE p_operation
                WHEN 'sessions' THEN
                    RETURN (SELECT min(value) FROM (
                        SELECT {S("StartTime")} AS value {sessions} AND {S("StartTime")} > p_now
                        UNION ALL SELECT {S("EndTime")} AS value {sessions} AND {S("EndTime")} > p_now
                    ) boundaries);
                WHEN 'remote' THEN
                    RETURN (SELECT min(value) FROM {sql.Table<AtprotoEventProjection>()} v
                        CROSS JOIN LATERAL (VALUES ({V("StartsAt")}),({V("EndsAt")})) boundaries(value)
                        WHERE {RemoteEligibility(sql)} AND value > p_now);
                WHEN 'locations' THEN
                    RETURN (SELECT p_now WHERE EXISTS (SELECT 1 {locations}));
                WHEN 'created' THEN
                    RETURN (SELECT min({L("CreatedAt")}) {locations} {disclosure} AND {L("CreatedAt")} > p_threshold);
                WHEN 'reveal' THEN
                    RETURN (SELECT min({L("RevealFullDetailsFromUtc")}) {locations} {disclosure}
                        AND {L("RevealFullDetailsFromUtc")} > p_now);
                ELSE RAISE EXCEPTION 'public_clock_operation_invalid' USING ERRCODE = '22023';
            END CASE;
            """;
    }

    // This is the response boundary's source predicate, not the discovery occurrence
    // predicate: missing ends, absent local-date projections and unpublished sibling
    // sessions must not narrow the original clock's conservative source envelope.
    private static string EligibleLocal(SourceSql sql, string alias)
    {
        string E(string property) => sql.Column<Event>(alias, property);
        string A(string property) => sql.Column<Actor>("a", property);
        return $"""
            {sql.Live<Event>(alias)}
            AND {E("EventStatusId")} = {(int)EventStatusEnum.Published}
            AND {E("VisibilityTypeId")} = {(int)VisibilityTypeEnum.Public}
            AND EXISTS (SELECT 1 FROM {sql.Table<Actor>()} a
                WHERE {A("Id")} = {E("ActorId")} AND {sql.Live<Actor>("a")} AND NOT {A("IsSuspended")}
                AND CASE WHEN {E("AtprotoRecordId")} IS NULL
                    OR EXISTS (SELECT 1 FROM {sql.Table<AtprotoOutboundRecordOwnership>()} o
                        WHERE {sql.Live<AtprotoOutboundRecordOwnership>("o")}
                            AND {sql.Column<AtprotoOutboundRecordOwnership>("o", "AtprotoRecordId")} = {E("AtprotoRecordId")}
                            AND {sql.Column<AtprotoOutboundRecordOwnership>("o", "SourceEntityType")} = 'Event'
                            AND {sql.Column<AtprotoOutboundRecordOwnership>("o", "SourceEntityId")} = {E("Id")})
                THEN
                    ({A("ActorTypeId")} = {(int)ActorTypeEnum.User} AND {A("UserId")} IS NOT NULL
                        AND EXISTS (SELECT 1 FROM {sql.Table<TenantUser>()} m WHERE {sql.Live<TenantUser>("m")}
                            AND {sql.Column<TenantUser>("m", "UserId")} = {A("UserId")}
                            AND {sql.Column<TenantUser>("m", "ActorId")} = {E("ActorId")}
                            AND {sql.Column<TenantUser>("m", "StatusId")} = {(int)TenantUserStatusEnum.Active}))
                    OR ({A("ActorTypeId")} = {(int)ActorTypeEnum.Organization} AND {A("OrganizationId")} IS NOT NULL
                        AND EXISTS (SELECT 1 FROM {sql.Table<OrganizationTenant>()} m WHERE {sql.Live<OrganizationTenant>("m")}
                            AND {sql.Column<OrganizationTenant>("m", "OrganizationId")} = {A("OrganizationId")}
                            AND {sql.Column<OrganizationTenant>("m", "ApprovalStatusId")} = {(int)ApprovalStatusEnum.Approved}
                            AND {sql.Column<OrganizationTenant>("m", "IsVisible")} AND NOT {sql.Column<OrganizationTenant>("m", "IsSuspended")}))
                    OR ({A("ActorTypeId")} = {(int)ActorTypeEnum.Group} AND {A("GroupId")} IS NOT NULL
                        AND EXISTS (SELECT 1 FROM {sql.Table<GroupTenant>()} m WHERE {sql.Live<GroupTenant>("m")}
                            AND {sql.Column<GroupTenant>("m", "GroupId")} = {A("GroupId")}
                            AND {sql.Column<GroupTenant>("m", "ApprovalStatusId")} = {(int)ApprovalStatusEnum.Approved}
                            AND {sql.Column<GroupTenant>("m", "IsVisible")} AND NOT {sql.Column<GroupTenant>("m", "IsSuspended")}))
                ELSE EXISTS (SELECT 1 FROM {sql.Table<AtprotoRecord>()} r
                    WHERE {sql.Column<AtprotoRecord>("r", "Id")} = {E("AtprotoRecordId")}
                        AND {sql.Column<AtprotoRecord>("r", "TombstonedAt")} IS NULL
                        AND {ImportedAuthority(sql, alias, "r")})
                END)
            """;
    }

    private static string ImportedAuthority(SourceSql sql, string eventAlias, string recordAlias) => $"""
        EXISTS (SELECT 1 FROM {sql.Table<AtprotoRecordTenantPresentation>()} p
            WHERE {sql.Live<AtprotoRecordTenantPresentation>("p")}
                AND {sql.Column<AtprotoRecordTenantPresentation>("p", "AtprotoRecordId")} = {sql.Column<AtprotoRecord>(recordAlias, "Id")}
                AND {sql.Column<AtprotoRecordTenantPresentation>("p", "IsVisible")}
                AND {sql.Column<AtprotoRecordTenantPresentation>("p", "SourceVersion")} = {sql.Column<AtprotoRecord>(recordAlias, "SourceVersion")})
        AND EXISTS (SELECT 1 FROM {sql.Table<AtprotoIdentity>()} i WHERE {sql.Live<AtprotoIdentity>("i")}
            AND {sql.Column<AtprotoIdentity>("i", "ActorId")} = {sql.Column<Event>(eventAlias, "ActorId")}
            AND {sql.Column<AtprotoIdentity>("i", "Did")} = {sql.Column<AtprotoRecord>(recordAlias, "Did")}
            AND {sql.Column<AtprotoIdentity>("i", "IsActive")} AND NOT {sql.Column<AtprotoIdentity>("i", "IsSuspended")})
        """;

    private static string RemoteEligibility(SourceSql sql)
    {
        string E(string property) => sql.Column<Event>("e", property);
        string R(string property) => sql.Column<AtprotoRecord>("r", property);
        string O(string property) => sql.Column<AtprotoOutboundRecordOwnership>("o", property);
        string A(string property) => sql.Column<Actor>("a", property);
        return $"""
            EXISTS (SELECT 1 FROM {sql.Table<AtprotoRecord>()} r
                WHERE {R("Id")} = {sql.Column<AtprotoEventProjection>("v", "AtprotoRecordId")} AND {R("TombstonedAt")} IS NULL
                AND CASE WHEN EXISTS (SELECT 1 FROM {sql.Table<AtprotoOutboundRecordOwnership>()} o
                    WHERE {sql.Live<AtprotoOutboundRecordOwnership>("o")} AND {O("AtprotoRecordId")} = {R("Id")}
                        AND {O("SourceEntityType")} = 'Event')
                THEN EXISTS (SELECT 1 FROM {sql.Table<Event>()} e WHERE {EligibleLocal(sql, "e")}
                    AND {E("AtprotoRecordId")} = {R("Id")}
                    AND EXISTS (SELECT 1 FROM {sql.Table<AtprotoOutboundRecordOwnership>()} o
                        WHERE {sql.Live<AtprotoOutboundRecordOwnership>("o")} AND {O("AtprotoRecordId")} = {R("Id")}
                            AND {O("SourceEntityType")} = 'Event' AND {O("SourceEntityId")} = {E("Id")}))
                ELSE EXISTS (SELECT 1 FROM {sql.Table<Event>()} e JOIN {sql.Table<Actor>()} a ON {A("Id")} = {E("ActorId")}
                    WHERE {sql.Live<Event>("e")} AND {sql.Live<Actor>("a")} AND NOT {A("IsSuspended")}
                        AND {E("AtprotoRecordId")} = {R("Id")} AND {E("VisibilityTypeId")} = {(int)VisibilityTypeEnum.Public}
                        AND {E("EventStatusId")} NOT IN ({(int)EventStatusEnum.Moderated},{(int)EventStatusEnum.Archived})
                        AND {ImportedAuthority(sql, "e", "r")}) END)
            """;
    }
}
