namespace Explore.Application.Features.AiAssistant.Context;

public sealed class AiSafeDataContextSummaryPolicy
{
    private static readonly StringComparer FieldComparer = StringComparer.OrdinalIgnoreCase;

    private readonly AiSafeDataContextRegistry _registry;

    public AiSafeDataContextSummaryPolicy()
        : this(AiSafeDataContextRegistry.CreateDefault())
    {
    }

    public AiSafeDataContextSummaryPolicy(AiSafeDataContextRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public AiSafeDataContextSummaryResult ValidateRequest(string? contextKind, IReadOnlyCollection<string>? requestedFields)
    {
        var definition = _registry.Find(contextKind);
        if (definition is null)
        {
            return AiSafeDataContextSummaryResult.Failure(
                AiSafeDataContextFailureCodes.ContextKindNotAllowed,
                "AI data context kind is not allow-listed.");
        }

        var normalizedFields = NormalizeRequestedFields(requestedFields);
        if (normalizedFields.Count == 0)
        {
            return AiSafeDataContextSummaryResult.Success(definition.Fields.Select(field => field.Name).ToArray());
        }

        if (normalizedFields.Any(field => !definition.FieldNames.Contains(field)))
        {
            return AiSafeDataContextSummaryResult.Failure(
                AiSafeDataContextFailureCodes.ContextFieldNotAllowed,
                "AI data context field is not allow-listed.");
        }

        return AiSafeDataContextSummaryResult.Success(normalizedFields);
    }

    private static IReadOnlyList<string> NormalizeRequestedFields(IReadOnlyCollection<string>? requestedFields)
    {
        if (requestedFields is null || requestedFields.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>(requestedFields.Count);
        var seen = new HashSet<string>(FieldComparer);
        foreach (var requestedField in requestedFields)
        {
            if (string.IsNullOrWhiteSpace(requestedField))
            {
                normalized.Add(string.Empty);
                continue;
            }

            var trimmed = requestedField.Trim();
            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        return normalized;
    }
}

public sealed record AiSafeDataContextSummaryResult(
    bool Succeeded,
    IReadOnlyList<string> Fields,
    string? FailureCode,
    string? FailureMessage)
{
    public static AiSafeDataContextSummaryResult Success(IReadOnlyList<string> fields)
        => new(true, fields, null, null);

    public static AiSafeDataContextSummaryResult Failure(string failureCode, string failureMessage)
        => new(false, [], failureCode, failureMessage);
}
