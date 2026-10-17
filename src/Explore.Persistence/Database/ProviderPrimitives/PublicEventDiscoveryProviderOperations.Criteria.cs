using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.Events;
using Explore.Domain.Enums;
using Explore.Domain.Services.Discovery;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static partial class PublicEventDiscoveryProviderOperations
{
    // Each entry is one conjunction. Never key clauses by kind: repeated filters must survive.
    internal sealed record Clause(string Group, string Kind)
    {
        public int? Integer { get; init; }
        public int[] Integers { get; init; } = [];
        public Guid? Identifier { get; init; }
        public Guid[] Identifiers { get; init; } = [];
        public string? Text { get; init; }
        public DateOnly? Date { get; init; }
        public string? Namespace { get; init; }
        public string? Key { get; init; }
        public decimal? Min { get; init; }
        public decimal? Max { get; init; }
        public DateTimeOffset? From { get; init; }
        public DateTimeOffset? To { get; init; }
    }

    internal static Clause[] Compile(EventQuerySpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var clauses = new List<Clause>();
        foreach (var filter in specification.Filters)
        {
            var (group, kind, operand) = filter switch
            {
                EventFilter value => ("event", Defined(value.FilterType), value.Operand),
                IslamicAspectFilter value => ("islamic", Defined(value.FilterType), value.Operand),
                TechAspectFilter value => ("tech", Defined(value.FilterType), value.Operand),
                AspectPresenceFilter value => ("presence", Defined(value.FilterType), new EventFilterOperand()),
                _ => throw new ArgumentException("Native discovery accepts only concrete semantic filter factories.")
            };
            clauses.Add(new(group, kind)
            {
                Integer = operand.Integer,
                Integers = operand.Integers.IsDefault ? [] : operand.Integers.ToArray(),
                Identifier = operand.Identifier,
                Text = operand.Text,
                Date = operand.Date
            });
        }
        foreach (var filter in specification.SubqueryFilters)
        {
            if (filter.FilterType == EventSubqueryFilterType.Occurrence)
                continue;
            var clause = new Clause("subquery", Defined(filter.FilterType));
            clauses.Add(filter.FilterType switch
            {
                EventSubqueryFilterType.Category or EventSubqueryFilterType.Location =>
                    clause with { Identifier = (Guid)filter.Value },
                EventSubqueryFilterType.CategoriesIncludedAll or EventSubqueryFilterType.CategoriesIncludedAny
                    or EventSubqueryFilterType.CategoriesExcludedAll or EventSubqueryFilterType.CategoriesExcludedAny
                    or EventSubqueryFilterType.TagsIncludedAll or EventSubqueryFilterType.TagsIncludedAny
                    or EventSubqueryFilterType.TagsExcludedAll or EventSubqueryFilterType.TagsExcludedAny
                    or EventSubqueryFilterType.Locations => clause with { Identifiers = [.. (List<Guid>)filter.Value] },
                EventSubqueryFilterType.Language or EventSubqueryFilterType.RegistrationMode =>
                    clause with { Integer = (int)filter.Value },
                EventSubqueryFilterType.Languages or EventSubqueryFilterType.RegistrationModes =>
                    clause with { Integers = [.. (List<int>)filter.Value] },
                EventSubqueryFilterType.FutureOnly => clause with { From = (DateTimeOffset)filter.Value },
                EventSubqueryFilterType.CurrentOrUpcomingPublishedSession => clause,
                EventSubqueryFilterType.TemporalView => clause,
                _ => throw new ArgumentException("Unknown native discovery subquery.")
            });
            if (filter.FilterType == EventSubqueryFilterType.TemporalView)
            {
                var (view, now) = ((TemporalView, DateTimeOffset))filter.Value;
                Defined(view);
                clauses[^1] = clause with { Integer = (int)view, From = now };
            }
        }
        foreach (var filter in specification.ProjectionFilters)
        {
            if (filter.ExposureCeiling != ExposureLevel.Public)
                throw new UnauthorizedAccessException("Public discovery cannot probe nonpublic custom properties.");
            var clause = new Clause("property", Defined(filter.FilterType));
            clauses.Add(filter.FilterType switch
            {
                EventCustomPropertyProjectionFilterType.ExactMatch or EventCustomPropertyProjectionFilterType.TextSearch =>
                    TextProperty(clause, ((string, string, string))filter.Value),
                EventCustomPropertyProjectionFilterType.GlobalTextSearch =>
                    clause with { Text = "%" + ((string)filter.Value).ToLowerInvariant() + "%" },
                EventCustomPropertyProjectionFilterType.Exists or EventCustomPropertyProjectionFilterType.BooleanTrue =>
                    clause with
                    {
                        Namespace = (((string, string))filter.Value).Item1,
                        Key = (((string, string))filter.Value).Item2
                    },
                EventCustomPropertyProjectionFilterType.OptionMatch =>
                    OptionProperty(clause, ((string, string, Guid))filter.Value),
                EventCustomPropertyProjectionFilterType.OptionsMatchAny =>
                    OptionsProperty(clause, ((string, string, List<Guid>))filter.Value),
                EventCustomPropertyProjectionFilterType.NumberRange =>
                    NumberProperty(clause, ((string, string, decimal?, decimal?))filter.Value),
                EventCustomPropertyProjectionFilterType.DateRange =>
                    DateProperty(clause, ((string, string, DateTimeOffset?, DateTimeOffset?))filter.Value),
                _ => throw new ArgumentException("Unknown native discovery custom property filter.")
            });
        }
        if (clauses.Count > 128 || specification.ProjectionFilters.Count > 11
            || clauses.Any(clause => clause.Integers.Length > 50 || clause.Identifiers.Length > 50
                || clause.Text?.Length > (clause.Group == "property" ? 502 : 200)
                || clause.Namespace?.Length > 100 || clause.Key?.Length > 100))
            throw new ArgumentOutOfRangeException(nameof(specification), "Discovery criteria exceed their finite bounds.");
        return [.. clauses];
    }

    private static Clause TextProperty(Clause clause, (string Namespace, string Key, string Text) value) =>
        clause with
        {
            Namespace = value.Namespace,
            Key = value.Key,
            Text = clause.Kind == nameof(EventCustomPropertyProjectionFilterType.TextSearch)
                ? "%" + value.Text.ToLowerInvariant() + "%" : value.Text
        };
    private static Clause OptionProperty(Clause clause, (string Namespace, string Key, Guid Id) value) =>
        clause with { Namespace = value.Namespace, Key = value.Key, Identifier = value.Id };
    private static Clause OptionsProperty(Clause clause, (string Namespace, string Key, List<Guid> Ids) value) =>
        clause with { Namespace = value.Namespace, Key = value.Key, Identifiers = [.. value.Ids] };
    private static Clause NumberProperty(Clause clause, (string Namespace, string Key, decimal? Min, decimal? Max) value) =>
        clause with { Namespace = value.Namespace, Key = value.Key, Min = value.Min, Max = value.Max };
    private static Clause DateProperty(Clause clause, (string Namespace, string Key, DateTimeOffset? From, DateTimeOffset? To) value) =>
        clause with { Namespace = value.Namespace, Key = value.Key, From = value.From, To = value.To };

    private static string Defined<T>(T value) where T : struct, Enum =>
        Enum.IsDefined(value) ? value.ToString() : throw new ArgumentOutOfRangeException(nameof(value));

    private static string Sort(EventQuerySpecification specification) =>
        specification.Sort is null || specification.Sort == EventSort.Date || specification.Sort == EventSort.Temporal ? "Date"
        : specification.Sort == EventSort.Title ? "Title"
        : specification.Sort == EventSort.Views ? "Views"
        : specification.Sort == EventSort.CreatedAt ? "CreatedAt"
        : throw new ArgumentException("Unknown native discovery sort.");

    private static object? Cursor(EventDiscoverySourceCursor? after) => after is null ? null : new
    {
        SourceKey = EventDiscoveryRank.SourceKey(after.Id),
        TitleKey = EventDiscoveryRank.TitleKey(after.Title),
        after.Views,
        CreatedAt = after.CreatedAtUtc,
        after.StartsAt
    };
}
