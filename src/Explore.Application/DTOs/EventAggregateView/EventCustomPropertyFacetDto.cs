using System.Collections.Immutable;
using System.Text.Json;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventAggregateView;

public sealed record EventCustomPropertyFacetDto
{
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public PropertyType PropertyType { get; init; }
    public ExposureLevel ExposureLevel { get; init; }
    private IReadOnlyList<JsonElement>? _values = ImmutableArray<JsonElement>.Empty;

    public IReadOnlyList<JsonElement> Values
    {
        get => _values!;
        init => _values = value?.ToImmutableArray();
    }
    public bool IsSearchable { get; init; }
    public bool IsFilterable { get; init; }
    public bool IsExportable { get; init; }
    public bool IsModerationRelevant { get; init; }
    public bool IsAnalyticsRelevant { get; init; }
}
