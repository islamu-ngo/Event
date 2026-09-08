namespace Explore.Application.Features.AiAssistant.Context;

public sealed class AiSafeDataContextDefinition
{
    public AiSafeDataContextDefinition(
        string contextKind,
        string sourceProjection,
        IEnumerable<AiSafeDataContextField> fields)
    {
        if (string.IsNullOrWhiteSpace(contextKind))
        {
            throw new ArgumentException("AI data context kind is required.", nameof(contextKind));
        }

        if (string.IsNullOrWhiteSpace(sourceProjection))
        {
            throw new ArgumentException("AI data context source projection is required.", nameof(sourceProjection));
        }

        ArgumentNullException.ThrowIfNull(fields);

        var fieldList = fields.ToArray();
        if (fieldList.Length == 0)
        {
            throw new ArgumentException("At least one AI data context field is required.", nameof(fields));
        }

        var duplicateField = fieldList
            .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (!string.IsNullOrWhiteSpace(duplicateField))
        {
            throw new ArgumentException("AI data context fields must be unique within a context definition.", nameof(fields));
        }

        ContextKind = contextKind.Trim();
        SourceProjection = sourceProjection.Trim();
        Fields = fieldList;
        FieldNames = fieldList.Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string ContextKind { get; }

    public string SourceProjection { get; }

    public IReadOnlyList<AiSafeDataContextField> Fields { get; }

    public IReadOnlySet<string> FieldNames { get; }
}
