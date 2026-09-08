namespace Explore.Application.Features.AiAssistant.Context;

public sealed class AiSafeDataContextRegistry
{
    public const string EventReferenceSummaryContextKind = "event-reference-summary";
    public const string EventReferenceSummarySourceProjection = "AiReferenceSearchResultDto";

    private readonly IReadOnlyDictionary<string, AiSafeDataContextDefinition> _definitions;

    public static AiSafeDataContextRegistry CreateDefault()
        => new([
            new AiSafeDataContextDefinition(
                EventReferenceSummaryContextKind,
                EventReferenceSummarySourceProjection,
                [
                    new AiSafeDataContextField("kind", "Reference kind used for prompt grouping."),
                    new AiSafeDataContextField("referenceId", "Stable reference identifier used for citations.", isCitationField: true),
                    new AiSafeDataContextField("displayName", "Bounded public display title."),
                    new AiSafeDataContextField("summary", "Bounded public summary only."),
                    new AiSafeDataContextField("firstSessionDate", "First known public session date."),
                    new AiSafeDataContextField("lastSessionDate", "Last known public session date."),
                    new AiSafeDataContextField("visibility", "Public visibility label already exposed by the reference projection."),
                    new AiSafeDataContextField("format", "Public event format label already exposed by the reference projection.")
                ])
        ]);

    public AiSafeDataContextRegistry(IEnumerable<AiSafeDataContextDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var definitionList = definitions.ToArray();
        var duplicateContextKind = definitionList
            .GroupBy(definition => definition.ContextKind, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (!string.IsNullOrWhiteSpace(duplicateContextKind))
        {
            throw new ArgumentException("AI data context kinds must be unique.", nameof(definitions));
        }

        Definitions = definitionList;
        _definitions = definitionList.ToDictionary(definition => definition.ContextKind, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AiSafeDataContextDefinition> Definitions { get; }

    public AiSafeDataContextDefinition? Find(string? contextKind)
    {
        if (string.IsNullOrWhiteSpace(contextKind))
        {
            return null;
        }

        return _definitions.GetValueOrDefault(contextKind.Trim());
    }
}
