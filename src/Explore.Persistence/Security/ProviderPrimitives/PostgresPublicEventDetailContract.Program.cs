using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventDetailContract
{
    // Program eligibility intentionally retains the existing closed-session rule,
    // not discovery's broader explicit open-ended occurrence rule.
    private static string ProgramSessions(SourceSql sql)
    {
        string S(string property) => sql.Column<EventSession>("ps", property);
        string aspect = $"""
            (SELECT {sql.Projection<EventSessionIslamicAspect>("ia", "EventSessionId", "StartTimeType",
                "ReferencePrayer", "OffsetMinutes", "EndReferencePrayer", "EndOffsetMinutes",
                "RequiresWudu", "RitualRequirementsJson")}
                FROM {sql.Table<EventSessionIslamicAspect>()} ia
                WHERE {sql.Column<EventSessionIslamicAspect>("ia", "EventSessionId")} = {S("Id")})
            """;
        string assignments = $"""
            COALESCE((SELECT pg_catalog.jsonb_agg(
                {sql.Projection<EventSessionGroupSession>("pa", "Id", "TenantId", "EventId",
                    "EventSessionId", "EventSessionGroupId", "IsPrimary", "SortOrder")})
                FROM {sql.Table<EventSessionGroupSession>()} pa
                JOIN {sql.Table<EventSessionGroup>()} pg
                    ON {sql.Column<EventSessionGroup>("pg", "Id")} = {sql.Column<EventSessionGroupSession>("pa", "EventSessionGroupId")}
                WHERE {sql.Live<EventSessionGroupSession>("pa")} AND {sql.Live<EventSessionGroup>("pg")}
                    AND {sql.Column<EventSessionGroupSession>("pa", "EventId")} = {S("EventId")}
                    AND {sql.Column<EventSessionGroupSession>("pa", "EventSessionId")} = {S("Id")}
                    AND {sql.Column<EventSessionGroup>("pg", "EventId")} = {S("EventId")}
                    AND {sql.Column<EventSessionGroup>("pg", "IsPublished")}), '[]'::jsonb)
            """;
        return $"""
            COALESCE((SELECT pg_catalog.jsonb_agg(
                {sql.Projection<EventSession>("ps", "Id", "TenantId", "EventId", "EventDayId",
                    "ConcurrencyStamp", "Title", "Description", "Slug", "StartTime", "EndTime", "EndTimeType",
                    "LocalStartDate", "LocalEndDate", "LocalStartTime", "LocalEndTime",
                    "LocalStartMinuteOfDay", "LocalEndMinuteOfDay", "SortOrder", "EventSessionKindId",
                    "EventSessionStatusId", "RegistrationModeId", "MaxAudienceAttendees", "CurrentAudienceAttendees",
                    "EventLocationId", "RoomId", "FeaturedImageId")}
                || pg_catalog.jsonb_build_object(
                    {Lookup<EventSessionKind>(sql, "EventSessionKind", S("EventSessionKindId"))},
                    {Lookup<RegistrationMode>(sql, "RegistrationMode", S("RegistrationModeId"))},
                    'IslamicAspect',{aspect}, 'SessionGroups',{assignments})
                ORDER BY {S("StartTime")}, {S("Id")})
                FROM {sql.Table<EventSession>()} ps
                WHERE {sql.Live<EventSession>("ps")} AND {S("EventId")} = {sql.Column<Event>("e", "Id")}
                    AND {S("EventSessionStatusId")} = {(int)EventSessionStatusEnum.Published}
                    AND {S("StartTime")} IS NOT NULL AND {S("EndTime")} IS NOT NULL
                    AND {PublishedProgramDay(sql, S("EventDayId"), S("EventId"))}), '[]'::jsonb)
            """;
    }

    private static string PublishedProgramDay(SourceSql sql, string dayId, string eventId) => $"""
        ({dayId} IS NULL OR EXISTS (SELECT 1 FROM {sql.Table<EventDay>()} pd
            WHERE {sql.Live<EventDay>("pd")} AND {sql.Column<EventDay>("pd", "Id")} = {dayId}
                AND {sql.Column<EventDay>("pd", "EventId")} = {eventId}
                AND {sql.Column<EventDay>("pd", "IsPublished")}))
        """;

    private static string ProgramGroups(SourceSql sql) => $"""
        COALESCE((SELECT pg_catalog.jsonb_agg(
            {sql.Projection<EventSessionGroup>("pg", "Id", "TenantId", "EventId", "Name", "Slug", "Description",
                "Color", "SortOrder", "IsPublished", "EventLocationId", "RoomId")}
            ORDER BY {sql.Column<EventSessionGroup>("pg", "SortOrder")}, {sql.Column<EventSessionGroup>("pg", "Name")})
            FROM {sql.Table<EventSessionGroup>()} pg WHERE {sql.Live<EventSessionGroup>("pg")}
                AND {sql.Column<EventSessionGroup>("pg", "EventId")} = {sql.Column<Event>("e", "Id")}
                AND {sql.Column<EventSessionGroup>("pg", "IsPublished")}), '[]'::jsonb)
        """;

    private static string ProgramAgenda(SourceSql sql) => $"""
        COALESCE((SELECT pg_catalog.jsonb_agg(
            {sql.Projection<EventAgendaItem>("pi", "Id", "TenantId", "EventId", "EventDayId", "Title", "Description",
                "StartTime", "EndTime", "LocalStartDate", "LocalEndDate", "LocalStartTime", "LocalEndTime",
                "SortOrder", "EventLocationId", "RoomId", "KindId")}
            ORDER BY {sql.Column<EventAgendaItem>("pi", "SortOrder")}, {sql.Column<EventAgendaItem>("pi", "StartTime")})
            FROM {sql.Table<EventAgendaItem>()} pi WHERE {sql.Live<EventAgendaItem>("pi")}
                AND {sql.Column<EventAgendaItem>("pi", "EventId")} = {sql.Column<Event>("e", "Id")}
                AND {PublishedProgramDay(sql, sql.Column<EventAgendaItem>("pi", "EventDayId"), sql.Column<EventAgendaItem>("pi", "EventId"))}), '[]'::jsonb)
        """;

    private static void MaterializeProgram(ExploreDbContext context, Event parent, JsonElement row)
    {
        var groups = row.GetProperty("SessionGroups").EnumerateArray().Select(value =>
            PublicEventDiscoveryProviderOperations.Materialize<EventSessionGroup>(context, value)).ToDictionary(group => group.Id);
        foreach (var group in groups.Values)
        {
            group.Event = parent;
            parent.SessionGroups.Add(group);
        }
        foreach (var value in row.GetProperty("Sessions").EnumerateArray())
        {
            var session = PublicEventDiscoveryProviderOperations.Materialize<EventSession>(context, value);
            session.Event = parent;
            session.EventSessionKind = Optional<EventSessionKind>(value, "EventSessionKind");
            session.RegistrationMode = Optional<RegistrationMode>(value, "RegistrationMode");
            session.IslamicAspect = Optional<EventSessionIslamicAspect>(value, "IslamicAspect");
            foreach (var assignmentValue in value.GetProperty("SessionGroups").EnumerateArray())
            {
                var assignment = PublicEventDiscoveryProviderOperations.Materialize<EventSessionGroupSession>(context, assignmentValue);
                assignment.Event = parent;
                assignment.EventSession = session;
                assignment.EventSessionGroup = groups[assignment.EventSessionGroupId];
                session.SessionGroups.Add(assignment);
                assignment.EventSessionGroup.Sessions.Add(assignment);
            }
            parent.Sessions.Add(session);
        }
        foreach (var value in row.GetProperty("AgendaItems").EnumerateArray())
        {
            var item = PublicEventDiscoveryProviderOperations.Materialize<EventAgendaItem>(context, value);
            item.Event = parent;
            parent.AgendaItems.Add(item);
        }

        T? Optional<T>(JsonElement value, string property) where T : class =>
            value.GetProperty(property) is { ValueKind: not JsonValueKind.Null } nested
                ? PublicEventDiscoveryProviderOperations.Materialize<T>(context, nested) : null;
    }
}
