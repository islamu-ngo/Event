using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventDiscoveryContract
{
    private static string EligibleLocal(SourceSql sql, string alias)
    {
        string E(string property) => sql.Column<Event>(alias, property);
        string A(string property) => sql.Column<Actor>("a", property);
        string Ownership(string record) => $"""
            EXISTS (SELECT 1 FROM {sql.Table<AtprotoOutboundRecordOwnership>()} o
                WHERE {sql.Live<AtprotoOutboundRecordOwnership>("o")}
                AND {sql.Column<AtprotoOutboundRecordOwnership>("o", "AtprotoRecordId")} = {record}
                AND {sql.Column<AtprotoOutboundRecordOwnership>("o", "SourceEntityType")} = 'Event'
                AND {sql.Column<AtprotoOutboundRecordOwnership>("o", "SourceEntityId")} = {E("Id")})
            """;
        return $"""
            {sql.Live<Event>(alias)}
            AND {E("EventStatusId")} = {(int)EventStatusEnum.Published}
            AND {E("VisibilityTypeId")} = {(int)VisibilityTypeEnum.Public}
            AND EXISTS (SELECT 1 FROM {sql.Table<Actor>()} a
                WHERE {A("Id")} = {E("ActorId")} AND {sql.Live<Actor>("a")} AND NOT {A("IsSuspended")}
                AND CASE WHEN {E("AtprotoRecordId")} IS NULL OR {Ownership(E("AtprotoRecordId"))} THEN
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
                        AND {ImportedAuthority(sql, alias, "a", "r")})
                END)
            """;
    }

    private static string ImportedAuthority(SourceSql sql, string eventAlias, string actorAlias, string recordAlias) => $"""
        EXISTS (SELECT 1 FROM {sql.Table<AtprotoRecordTenantPresentation>()} p
            WHERE {sql.Live<AtprotoRecordTenantPresentation>("p")}
            AND {sql.Column<AtprotoRecordTenantPresentation>("p", "AtprotoRecordId")} = {sql.Column<AtprotoRecord>(recordAlias, "Id")}
            AND {sql.Column<AtprotoRecordTenantPresentation>("p", "IsVisible")}
            AND {sql.Column<AtprotoRecordTenantPresentation>("p", "SourceVersion")} = {sql.Column<AtprotoRecord>(recordAlias, "SourceVersion")})
        AND EXISTS (SELECT 1 FROM {sql.Table<AtprotoIdentity>()} i WHERE {sql.Live<AtprotoIdentity>("i")}
            AND {sql.Column<AtprotoIdentity>("i", "ActorId")} = {sql.Column<Event>(eventAlias, "ActorId")}
            AND {sql.Column<AtprotoIdentity>("i", "ActorId")} = {sql.Column<Actor>(actorAlias, "Id")}
            AND {sql.Column<AtprotoIdentity>("i", "Did")} = {sql.Column<AtprotoRecord>(recordAlias, "Did")}
            AND {sql.Column<AtprotoIdentity>("i", "IsActive")} AND NOT {sql.Column<AtprotoIdentity>("i", "IsSuspended")})
        """;

    private static string Occurrences(SourceSql sql)
    {
        string S(string property) => sql.Column<EventSession>("s", property);
        string D(string property) => sql.Column<EventDay>("d", property);
        string L(string property) => sql.Column<EventLocation>("l", property);
        string P(string property) => sql.Column<Location>("p", property);
        return $"""
            SELECT {string.Join(",", new[] { "Id", "TenantId", "EventId", "EventSessionStatusId", "StartTime", "EndTime",
                "EndTimeType", "LocalStartDate", "LocalStartTime", "LocalEndDate", "LocalEndTime",
                "EventLocationId", "RoomId", "Title" }.Select(S))}
            FROM {sql.Table<EventSession>()} s
            WHERE {sql.Live<EventSession>("s")}
                AND {S("EventSessionStatusId")} = {(int)EventSessionStatusEnum.Published}
                AND {S("StartTime")} IS NOT NULL AND {S("LocalStartDate")} IS NOT NULL
                AND ({S("EndTime")} > {S("StartTime")} OR {S("EndTime")} IS NULL AND {S("EndTimeType")} = {(int)SessionEndTimeType.OpenEnded})
                AND ({S("EventDayId")} IS NULL OR EXISTS (SELECT 1 FROM {sql.Table<EventDay>()} d
                    WHERE {sql.Live<EventDay>("d")} AND {D("Id")} = {S("EventDayId")} AND {D("IsPublished")}
                        AND {D("EventId")} = {S("EventId")} AND {D("LocalDate")} = {S("LocalStartDate")}))
                AND ((p_request->'Occurrence'->>'DateTo') IS NULL
                    OR {S("LocalStartDate")} <= (p_request->'Occurrence'->>'DateTo')::date)
                AND ((p_request->'Occurrence'->>'DateFrom') IS NULL
                    OR {S("EndTime")} IS NULL AND {S("EndTimeType")} = {(int)SessionEndTimeType.OpenEnded}
                    OR {S("LocalEndDate")} > (p_request->'Occurrence'->>'DateFrom')::date
                    OR {S("LocalEndDate")} = (p_request->'Occurrence'->>'DateFrom')::date AND {S("LocalEndTime")} > '00:00'::time)
                AND CASE (p_request->'Occurrence'->>'View')::integer
                    WHEN {(int)TemporalView.Upcoming} THEN {S("StartTime")} > (p_request->'Occurrence'->>'Now')::timestamptz
                    WHEN {(int)TemporalView.Ongoing} THEN {S("StartTime")} <= (p_request->'Occurrence'->>'Now')::timestamptz
                        AND ({S("EndTime")} > (p_request->'Occurrence'->>'Now')::timestamptz
                            OR {S("EndTime")} IS NULL AND {S("EndTimeType")} = {(int)SessionEndTimeType.OpenEnded})
                    WHEN {(int)TemporalView.Past} THEN {S("EndTime")} <= (p_request->'Occurrence'->>'Now')::timestamptz
                    WHEN {(int)TemporalView.UpcomingAndOngoing} THEN {S("EndTime")} > (p_request->'Occurrence'->>'Now')::timestamptz
                        OR {S("EndTime")} IS NULL AND {S("EndTimeType")} = {(int)SessionEndTimeType.OpenEnded}
                    WHEN {(int)TemporalView.All} THEN true ELSE false END
                AND (p_request->'Occurrence'->'LocationIds' = 'null'::jsonb
                    OR EXISTS (SELECT 1 FROM {sql.Table<EventLocation>()} l JOIN {sql.Table<Location>()} p
                        ON {P("Id")} = {L("LocationId")} WHERE {sql.Live<EventLocation>("l")} AND {sql.Live<Location>("p")}
                        AND {L("Id")} = {S("EventLocationId")} AND {L("EventId")} = {S("EventId")}
                        AND {L("LocationId")} = {S("LocationId")} AND {L("ShowCity")} AND {L("ShowCountry")}
                        AND NOT {L("IsToBeAnnounced")} AND NOT {L("NeedsPrivacyReview")}
                        AND {P("LocationPrivacyStateId")} = {(int)LocationPrivacyStateEnum.Active}
                        AND {P("LocationKindId")} <> {(int)LocationKindEnum.PrivateHome}
                        AND {L("LocationId")} IN (SELECT value::uuid
                            FROM pg_catalog.jsonb_array_elements_text(p_request->'Occurrence'->'LocationIds'))))
            """;
    }

    private static string LocalQuery(SourceSql sql, bool members, bool paged = false)
    {
        string E(string property) => sql.Column<Event>("e", property);
        string S(string property) => sql.Column<EventSession>("s", property);
        string occurrence = Occurrences(sql);
        string source = E("DiscoverySourceSortKey");
        string pair = members ? $"""
            AND EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_request->'Members') pair
                WHERE (pair->>'EventId')::uuid = {E("Id")} AND (pair->>'SessionId')::uuid = {S("Id")})
            """ : "";
        string payload = LocalProjection(sql);
        string conditions = ClauseConditions(sql);
        string keyset = Keyset(E("DiscoveryTitleSortKey"), E("CreatedAt"), E("TotalViews"), "chosen.starts", source, remote: false);
        string order = Order(E("DiscoveryTitleSortKey"), E("CreatedAt"), E("TotalViews"), "chosen.starts", source);
        string selection = $"""
            FROM {sql.Table<Event>()} e
            JOIN LATERAL (
                SELECT {S("Id")} AS id, {S("StartTime")} AS starts,
                    (SELECT count(*)::integer FROM occurrences n WHERE n.{sql.Name<EventSession>("EventId")} = {E("Id")}) AS matches
                FROM occurrences s WHERE {S("EventId")} = {E("Id")} {pair}
                ORDER BY {S("StartTime")}, {S("Id")} LIMIT 1
            ) chosen ON true
            JOIN occurrences s ON {S("Id")} = chosen.id
            WHERE {EligibleLocal(sql, "e")} AND {conditions}
                {(members ? "" : $"AND {keyset}")}
            """;
        return paged ? $"""
            WITH occurrences AS NOT MATERIALIZED ({occurrence}),
            candidates AS MATERIALIZED (
                SELECT {E("Id")} AS event_id, chosen.id, chosen.matches,
                    row_number() OVER (ORDER BY {order}) AS ordinal
                {selection}
            )
            SELECT pg_catalog.jsonb_build_object(
                'TotalCount',(SELECT count(*)::integer FROM candidates),
                'Items',COALESCE((SELECT pg_catalog.jsonb_agg({payload} ORDER BY chosen.ordinal)
                    FROM (SELECT * FROM candidates ORDER BY ordinal
                        LIMIT (p_request->>'Take')::integer OFFSET (p_request->>'Offset')::integer) chosen
                    JOIN {sql.Table<Event>()} e ON {E("Id")} = chosen.event_id
                    JOIN occurrences s ON {S("Id")} = chosen.id),'[]'::jsonb))
            """ : $"""
            WITH occurrences AS NOT MATERIALIZED ({occurrence})
            SELECT {payload} {selection}
            ORDER BY {order} LIMIT {(members ? "100" : "(p_request->>'Take')::integer")}
            """;
    }

    private static string RemoteEligibility(SourceSql sql, bool includeLocalEchoes)
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
                        AND {ImportedAuthority(sql, "e", "a", "r")}) END)
            {(includeLocalEchoes ? "" : $"""
                AND NOT EXISTS (SELECT 1 FROM {sql.Table<Event>()} e WHERE {sql.Live<Event>("e")}
                    AND {E("AtprotoRecordId")} = {sql.Column<AtprotoEventProjection>("v", "AtprotoRecordId")}
                    AND {E("EventStatusId")} = {(int)EventStatusEnum.Published}
                    AND {E("VisibilityTypeId")} = {(int)VisibilityTypeEnum.Public})
                """)}
            """;
    }

    private static string RemoteQuery(SourceSql sql, bool members, bool visible)
    {
        string V(string property) => sql.Column<AtprotoEventProjection>("v", property);
        string criteria = visible ? "true" : $"""
            (p_request->>'SearchPattern' IS NULL OR lower({V("Name")}) LIKE (p_request->>'SearchPattern')
                OR lower({V("Description")}) LIKE (p_request->>'SearchPattern'))
            AND (p_request->>'DateFrom' IS NULL OR {V("StartsAt")} >= (p_request->>'DateFrom')::date::timestamp AT TIME ZONE 'UTC')
            AND (p_request->>'DateTo' IS NULL OR {V("StartsAt")} < ((p_request->>'DateTo')::date + 1)::timestamp AT TIME ZONE 'UTC')
            AND (pg_catalog.jsonb_array_length(p_request->'Modes') = 0
                OR {V("Mode")} IN (SELECT value FROM pg_catalog.jsonb_array_elements_text(p_request->'Modes')))
            AND CASE (p_request->>'Temporal')::integer
                WHEN {(int)AtprotoEventTemporalFilter.CurrentOrUpcoming} THEN {V("StartsAt")} IS NOT NULL
                    AND COALESCE({V("EndsAt")},{V("StartsAt")}) > (p_request->>'Now')::timestamptz
                WHEN {(int)AtprotoEventTemporalFilter.Upcoming} THEN {V("StartsAt")} > (p_request->>'Now')::timestamptz
                WHEN {(int)AtprotoEventTemporalFilter.Ongoing} THEN {V("StartsAt")} <= (p_request->>'Now')::timestamptz
                    AND {V("EndsAt")} > (p_request->>'Now')::timestamptz
                WHEN {(int)AtprotoEventTemporalFilter.Past} THEN {V("StartsAt")} IS NOT NULL
                    AND COALESCE({V("EndsAt")},{V("StartsAt")}) <= (p_request->>'Now')::timestamptz
                WHEN {(int)AtprotoEventTemporalFilter.All} THEN true ELSE false END
            """;
        return $"""
            SELECT {sql.Projection<AtprotoEventProjection>("v", "AtprotoRecordId", "Name", "DiscoveryTitleSortKey", "DiscoverySourceSortKey", "Description", "CreatedAt",
                "StartsAt", "EndsAt", "Mode", "Status", "RsvpExpected", "LocationSummary", "SourceUrl")}
            FROM {sql.Table<AtprotoEventProjection>()} v
            WHERE {RemoteEligibility(sql, visible)} AND {criteria}
                AND {(members || visible
                    ? $"{V("AtprotoRecordId")} IN (SELECT value::uuid FROM pg_catalog.jsonb_array_elements_text(p_request->'Members'))"
                    : Keyset(V("DiscoveryTitleSortKey"), V("CreatedAt"), "0", V("StartsAt"), V("DiscoverySourceSortKey"), remote: true))}
            ORDER BY {(visible ? V("AtprotoRecordId") : Order(V("DiscoveryTitleSortKey"), V("CreatedAt"), "0", V("StartsAt"), V("DiscoverySourceSortKey")))}
            LIMIT {(members || visible ? "100" : "(p_request->>'Take')::integer")}
            """;
    }

    private static string Keyset(string title, string created, string views, string instant, string source, bool remote) => $"""
        (p_request->'After' = 'null'::jsonb OR CASE p_request->>'Sort'
            WHEN 'Title' THEN
                CASE WHEN (p_request->>'Descending')::boolean THEN {title} < (p_request->'After'->>'TitleKey') COLLATE "C"
                    ELSE {title} > (p_request->'After'->>'TitleKey') COLLATE "C" END
                OR {title} = (p_request->'After'->>'TitleKey') COLLATE "C" AND {source} > (p_request->'After'->>'SourceKey') COLLATE "C"
            WHEN 'CreatedAt' THEN
                CASE WHEN (p_request->>'Descending')::boolean THEN {created} < (p_request->'After'->>'CreatedAt')::timestamptz
                    ELSE {created} > (p_request->'After'->>'CreatedAt')::timestamptz END
                OR {created} = (p_request->'After'->>'CreatedAt')::timestamptz AND {source} > (p_request->'After'->>'SourceKey') COLLATE "C"
            WHEN 'Views' THEN {(remote ? $"{source} > (p_request->'After'->>'SourceKey') COLLATE \"C\"" : $"""
                CASE WHEN (p_request->>'Descending')::boolean THEN {views} < (p_request->'After'->>'Views')::integer
                    ELSE {views} > (p_request->'After'->>'Views')::integer END
                OR {views} = (p_request->'After'->>'Views')::integer AND {source} > (p_request->'After'->>'SourceKey') COLLATE "C"
                """)}
            ELSE CASE WHEN (p_request->'After'->>'StartsAt') IS NULL THEN
                CASE WHEN (p_request->>'Descending')::boolean THEN {instant} IS NULL
                    AND {source} > (p_request->'After'->>'SourceKey') COLLATE "C"
                ELSE {instant} IS NOT NULL OR {source} > (p_request->'After'->>'SourceKey') COLLATE "C" END
            ELSE CASE WHEN (p_request->>'Descending')::boolean THEN
                    {instant} IS NULL OR {instant} < (p_request->'After'->>'StartsAt')::timestamptz
                ELSE {instant} > (p_request->'After'->>'StartsAt')::timestamptz END
                OR {instant} = (p_request->'After'->>'StartsAt')::timestamptz
                    AND {source} > (p_request->'After'->>'SourceKey') COLLATE "C" END END)
        """;

    private static string Order(string title, string created, string views, string instant, string source) => $"""
        CASE WHEN p_request->>'Sort' = 'Title' AND NOT (p_request->>'Descending')::boolean THEN {title} END ASC,
        CASE WHEN p_request->>'Sort' = 'Title' AND (p_request->>'Descending')::boolean THEN {title} END DESC,
        CASE WHEN p_request->>'Sort' = 'CreatedAt' AND NOT (p_request->>'Descending')::boolean THEN {created} END ASC,
        CASE WHEN p_request->>'Sort' = 'CreatedAt' AND (p_request->>'Descending')::boolean THEN {created} END DESC,
        CASE WHEN p_request->>'Sort' = 'Views' AND NOT (p_request->>'Descending')::boolean THEN {views} END ASC,
        CASE WHEN p_request->>'Sort' = 'Views' AND (p_request->>'Descending')::boolean THEN {views} END DESC,
        CASE WHEN p_request->>'Sort' = 'Date' AND NOT (p_request->>'Descending')::boolean THEN {instant} END ASC NULLS FIRST,
        CASE WHEN p_request->>'Sort' = 'Date' AND (p_request->>'Descending')::boolean THEN {instant} END DESC NULLS LAST,
        {source} ASC
        """;
}
