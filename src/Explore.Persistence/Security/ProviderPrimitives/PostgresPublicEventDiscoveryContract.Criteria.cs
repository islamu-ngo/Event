using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventDiscoveryContract
{
    private static string ClauseConditions(SourceSql sql)
    {
        string E(string property) => sql.Column<Event>("e", property);
        string Integer(string column) => $"{column} = (c->>'Integer')::integer";
        string Integers(string column) => $"{column} IN (SELECT value::integer FROM pg_catalog.jsonb_array_elements_text(c->'Integers'))";
        string Contains(string column) => $"strpos({column},c->>'Text') > 0";
        var eventCases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (single, many, property) in new[]
        {
            (EventFilterType.EventType, EventFilterType.EventTypes, nameof(Event.EventTypeId)),
            (EventFilterType.Format, EventFilterType.Formats, nameof(Event.EventFormatId)),
            (EventFilterType.Madhab, EventFilterType.Madhabs, nameof(Event.MadhabId)),
            (EventFilterType.AudienceGender, EventFilterType.AudienceGenders, nameof(Event.AudienceGenderId)),
            (EventFilterType.AudienceAge, EventFilterType.AudienceAges, nameof(Event.AudienceAgeId)),
            (EventFilterType.Status, EventFilterType.Statuses, nameof(Event.EventStatusId))
        })
        {
            eventCases.Add(single.ToString(), Integer(E(property)));
            eventCases.Add(many.ToString(), Integers(E(property)));
        }
        eventCases.Add(nameof(EventFilterType.Visibility), Integer(E("VisibilityTypeId")));
        eventCases.Add(nameof(EventFilterType.Actor), $"{E("ActorId")} = (c->>'Identifier')::uuid");
        eventCases.Add(nameof(EventFilterType.SearchTerm),
            $"({Contains(E("Title"))} OR {Contains(E("Description"))} OR {Contains(E("Content"))})");
        eventCases.Add(nameof(EventFilterType.DateFrom), $"{E("FirstSessionDate")} >= (c->>'Date')::date");
        eventCases.Add(nameof(EventFilterType.DateTo), $"{E("FirstSessionDate")} <= (c->>'Date')::date");
        eventCases.Add(nameof(EventFilterType.PubliclyDiscoverable), $"""
            {E("VisibilityTypeId")} = {(int)VisibilityTypeEnum.Public}
                AND {E("EventStatusId")} NOT IN ({(int)EventStatusEnum.Draft},{(int)EventStatusEnum.Moderated},{(int)EventStatusEnum.Archived})
            """);
        eventCases.Add(nameof(EventFilterType.Free), $"""
            EXISTS (SELECT 1 FROM {sql.Table<EventParticipationConfiguration>()} pc
                WHERE {sql.Live<EventParticipationConfiguration>("pc")} AND {sql.Column<EventParticipationConfiguration>("pc", "Id")} = {E("Id")}
                AND {sql.Column<EventParticipationConfiguration>("pc", "ParticipationHandlingModeId")} = {(int)ParticipationHandlingModeEnum.PlatformManaged})
            AND EXISTS (SELECT 1 FROM {sql.Table<EventTicketCatalogVersion>()} cat
                JOIN {sql.Table<EventTicketType>()} tt ON {sql.Column<EventTicketType>("tt", "CatalogId")} = {sql.Column<EventTicketCatalogVersion>("cat", "Id")}
                WHERE {sql.Live<EventTicketCatalogVersion>("cat")} AND {sql.Live<EventTicketType>("tt")}
                    AND {sql.Column<EventTicketCatalogVersion>("cat", "EventId")} = {E("Id")}
                    AND {sql.Column<EventTicketCatalogVersion>("cat", "TicketCatalogStatusId")} = {(int)TicketCatalogStatusEnum.Published}
                    AND ({sql.Column<EventTicketType>("tt", "TicketPricingModeId")} = {(int)TicketPricingModeEnum.Free}
                        OR {sql.Column<EventTicketType>("tt", "TicketPricingModeId")} IN ({(int)TicketPricingModeEnum.Donation},{(int)TicketPricingModeEnum.PayWhatYouCan})
                            AND COALESCE({sql.Column<EventTicketType>("tt", "MinimumPriceMinor")},0) = 0
                        OR {sql.Column<EventTicketType>("tt", "TicketPricingModeId")} = {(int)TicketPricingModeEnum.SlidingScale}
                            AND {sql.Column<EventTicketType>("tt", "MinimumPriceMinor")} = 0))
            """);
        RequireKinds<EventFilterType>(eventCases);

        string I(string property) => sql.Column<EventIslamicAspect>("ia", property);
        var islamic = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(IslamicAspectFilterType.Madhab)] = Integer(I("MadhabId")),
            [nameof(IslamicAspectFilterType.GenderMode)] = Integer(I("GenderMode")),
            [nameof(IslamicAspectFilterType.GenderModes)] = Integers(I("GenderMode")),
            [nameof(IslamicAspectFilterType.IncludesQuranRecitation)] = I("IncludesQuranRecitation"),
            [nameof(IslamicAspectFilterType.ReferencePrayer)] = Integer(I("ReferencePrayer")),
            [nameof(IslamicAspectFilterType.ReferencePrayers)] = Integers(I("ReferencePrayer")),
            [nameof(IslamicAspectFilterType.PrimaryLanguage)] = Integer(I("PrimaryLanguageId")),
            [nameof(IslamicAspectFilterType.PrimaryLanguages)] = Integers(I("PrimaryLanguageId"))
        };
        RequireKinds<IslamicAspectFilterType>(islamic);
        string T(string property) => sql.Column<EventTechAspect>("ta", property);
        var tech = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(TechAspectFilterType.SkillLevel)] = Integer(T("SkillLevel")),
            [nameof(TechAspectFilterType.IsCodingCompetition)] = T("IsCodingCompetition"),
            [nameof(TechAspectFilterType.IsHackathon)] = $"{T("HackathonTrack")} IS NOT NULL",
            [nameof(TechAspectFilterType.HackathonTrack)] = Contains(T("HackathonTrack")),
            [nameof(TechAspectFilterType.RequiresLaptop)] = T("RequiresLaptop"),
            [nameof(TechAspectFilterType.TechStack)] = Contains(T("TechStackTags")),
            [nameof(TechAspectFilterType.HasPrizePool)] = $"{T("PrizePool")} > 0"
        };
        RequireKinds<TechAspectFilterType>(tech);
        string hasIslamic = $"EXISTS (SELECT 1 FROM {sql.Table<EventIslamicAspect>()} ia WHERE {sql.Live<EventIslamicAspect>("ia")} AND {I("Id")} = {E("Id")})";
        string hasTech = $"EXISTS (SELECT 1 FROM {sql.Table<EventTechAspect>()} ta WHERE {sql.Live<EventTechAspect>("ta")} AND {T("Id")} = {E("Id")})";
        return $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_request->'Clauses') c
                WHERE (CASE c->>'Group'
                    WHEN 'event' THEN {Cases(eventCases)}
                    WHEN 'islamic' THEN EXISTS (SELECT 1 FROM {sql.Table<EventIslamicAspect>()} ia
                        WHERE {sql.Live<EventIslamicAspect>("ia")} AND {I("Id")} = {E("Id")} AND {Cases(islamic)})
                    WHEN 'tech' THEN EXISTS (SELECT 1 FROM {sql.Table<EventTechAspect>()} ta
                        WHERE {sql.Live<EventTechAspect>("ta")} AND {T("Id")} = {E("Id")} AND {Cases(tech)})
                    WHEN 'presence' THEN CASE c->>'Kind' WHEN 'Islamic' THEN {hasIslamic}
                        WHEN 'Tech' THEN {hasTech} WHEN 'Both' THEN {hasIslamic} AND {hasTech} ELSE false END
                    WHEN 'subquery' THEN {SubqueryCases(sql)}
                    WHEN 'property' THEN {PropertyCases(sql)}
                    ELSE false END) IS NOT TRUE)
            """;
    }

    private static string SubqueryCases(SourceSql sql)
    {
        string E(string property) => sql.Column<Event>("e", property);
        var cases = new Dictionary<string, string>(StringComparer.Ordinal);
        AddJunction<EventCategories>("Category", "Categories", "CategoryId");
        AddJunction<EventTags>(null, "Tags", "TagId");
        string S(string property) => sql.Column<EventSession>("facet", property);
        string facet = $"""
            {sql.Live<EventSession>("facet")} AND {S("EventId")} = {E("Id")}
            AND (NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_request->'Clauses') f
                WHERE f->>'Group' = 'event' AND f->>'Kind' = 'PubliclyDiscoverable')
                OR {S("EventSessionStatusId")} = {(int)EventSessionStatusEnum.Published} AND {S("StartTime")} IS NOT NULL)
            """;
        foreach (bool many in new[] { false, true })
        {
            string guidMatch = many
                ? $"IN (SELECT value::uuid FROM pg_catalog.jsonb_array_elements_text(c->'Identifiers'))"
                : "= (c->>'Identifier')::uuid";
            string intMatch = many
                ? $"IN (SELECT value::integer FROM pg_catalog.jsonb_array_elements_text(c->'Integers'))"
                : "= (c->>'Integer')::integer";
            cases.Add(many ? "Locations" : "Location",
                $"EXISTS (SELECT 1 FROM {sql.Table<EventSession>()} facet WHERE {facet} AND {S("LocationId")} {guidMatch})");
            cases.Add(many ? "RegistrationModes" : "RegistrationMode",
                $"EXISTS (SELECT 1 FROM {sql.Table<EventSession>()} facet WHERE {facet} AND {S("RegistrationModeId")} {intMatch})");
            cases.Add(many ? "Languages" : "Language", $"""
                EXISTS (SELECT 1 FROM {sql.Table<EventSession>()} facet WHERE {facet}
                    AND EXISTS (SELECT 1 FROM {sql.Table<EventSessionLanguage>()} lang
                        WHERE {sql.Live<EventSessionLanguage>("lang")}
                            AND {sql.Column<EventSessionLanguage>("lang", "EventSessionId")} = {S("Id")}
                            AND {sql.Column<EventSessionLanguage>("lang", "LanguageId")} {intMatch}))
                """);
        }
        cases.Add("FutureOnly", $"{E("LastSessionStartUtc")} IS NULL OR {E("LastSessionStartUtc")} > (c->>'From')::timestamptz");
        cases.Add("CurrentOrUpcomingPublishedSession", $"{E("LastSessionEndUtc")} > (p_request->'Occurrence'->>'Now')::timestamptz");
        cases.Add("TemporalView", $"""
            CASE (c->>'Integer')::integer
                WHEN {(int)TemporalView.Upcoming} THEN {E("FirstSessionStartUtc")} > (c->>'From')::timestamptz
                WHEN {(int)TemporalView.Ongoing} THEN {E("FirstSessionStartUtc")} <= (c->>'From')::timestamptz
                    AND {E("LastSessionEndUtc")} > (c->>'From')::timestamptz
                WHEN {(int)TemporalView.Past} THEN {E("LastSessionEndUtc")} <= (c->>'From')::timestamptz
                WHEN {(int)TemporalView.UpcomingAndOngoing} THEN {E("LastSessionEndUtc")} > (c->>'From')::timestamptz
                WHEN {(int)TemporalView.All} THEN true ELSE false END
            """);
        RequireKinds<EventSubqueryFilterType>(cases, nameof(EventSubqueryFilterType.Occurrence));
        return Cases(cases);

        void AddJunction<T>(string? single, string plural, string property)
        {
            string owner = $"{sql.Live<T>("j")} AND {sql.Column<T>("j", "EventId")} = {E("Id")}";
            string key = sql.Column<T>("j", property);
            string any = $"""
                EXISTS (SELECT 1 FROM {sql.Table<T>()} j WHERE {owner}
                    AND {key} IN (SELECT value::uuid FROM pg_catalog.jsonb_array_elements_text(c->'Identifiers')))
                """;
            if (single is not null)
                cases.Add(single, $"EXISTS (SELECT 1 FROM {sql.Table<T>()} j WHERE {owner} AND {key} = (c->>'Identifier')::uuid)");
            cases.Add(plural + "IncludedAny", any);
            cases.Add(plural + "ExcludedAny", "NOT " + any);
            cases.Add(plural + "IncludedAll", $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements_text(c->'Identifiers') wanted
                    WHERE NOT EXISTS (SELECT 1 FROM {sql.Table<T>()} j WHERE {owner} AND {key} = wanted.value::uuid))
                """);
            // Preserve existing duplicate-sensitive excluded-all count semantics.
            cases.Add(plural + "ExcludedAll", $"""
                (SELECT count(DISTINCT {key}) FROM {sql.Table<T>()} j WHERE {owner}
                    AND {key} IN (SELECT value::uuid FROM pg_catalog.jsonb_array_elements_text(c->'Identifiers')))
                    < pg_catalog.jsonb_array_length(c->'Identifiers')
                """);
        }
    }

    private static string PropertyCases(SourceSql sql)
    {
        string P(string property) => sql.Column<EventCustomPropertyProjection>("cp", property);
        var cases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ExactMatch"] = $"{P("IsFilterable")} AND {P("NormalizedValue")} = c->>'Text'",
            ["OptionMatch"] = $"{P("IsFilterable")} AND {P("OptionId")} = (c->>'Identifier')::uuid",
            ["OptionsMatchAny"] = $"{P("IsFilterable")} AND {P("OptionId")} IN (SELECT value::uuid FROM pg_catalog.jsonb_array_elements_text(c->'Identifiers'))",
            ["TextSearch"] = $"{P("IsSearchable")} AND {P("NormalizedValue")} LIKE (c->>'Text')",
            ["GlobalTextSearch"] = $"{P("IsSearchable")} AND {P("NormalizedValue")} LIKE (c->>'Text')",
            ["Exists"] = P("IsFilterable"),
            ["BooleanTrue"] = $"{P("IsFilterable")} AND {P("BooleanValue")} = true",
            ["NumberRange"] = $"""
                {P("IsFilterable")} AND {P("NumberValue")} IS NOT NULL
                    AND (c->>'Min' IS NULL OR {P("NumberValue")} >= (c->>'Min')::numeric)
                    AND (c->>'Max' IS NULL OR {P("NumberValue")} <= (c->>'Max')::numeric)
                """,
            ["DateRange"] = $"""
                {P("IsFilterable")} AND {P("DateTimeValue")} IS NOT NULL
                    AND (c->>'From' IS NULL OR {P("DateTimeValue")} >= (c->>'From')::timestamptz)
                    AND (c->>'To' IS NULL OR {P("DateTimeValue")} <= (c->>'To')::timestamptz)
                """
        };
        RequireKinds<EventCustomPropertyProjectionFilterType>(cases);
        return $"""
            EXISTS (SELECT 1 FROM {sql.Table<EventCustomPropertyProjection>()} cp WHERE {sql.Live<EventCustomPropertyProjection>("cp")}
                AND {P("EventId")} = {sql.Column<Event>("e", "Id")} AND {P("ExposureLevel")} = {sql.Literal<EventCustomPropertyProjection>("ExposureLevel", ExposureLevel.Public)}
                AND (c->>'Kind' = 'GlobalTextSearch' OR {P("Namespace")} = c->>'Namespace' AND {P("Key")} = c->>'Key')
                AND {Cases(cases)})
            """;
    }

    private static string Cases(IReadOnlyDictionary<string, string> cases) =>
        "(CASE c->>'Kind' " + string.Join(" ", cases.Select(pair => $"WHEN {Text(pair.Key)} THEN ({pair.Value})")) + " ELSE false END)";

    private static void RequireKinds<T>(IReadOnlyDictionary<string, string> cases, string? excluded = null) where T : struct, Enum
    {
        if (!Enum.GetNames<T>().Where(name => name != excluded).Order(StringComparer.Ordinal)
            .SequenceEqual(cases.Keys.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("public_discovery_filter_catalog_changed");
    }
}
