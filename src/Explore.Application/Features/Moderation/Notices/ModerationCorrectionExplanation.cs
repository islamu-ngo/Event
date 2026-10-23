using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Explore.Application.Features.Moderation.Notices;

/// <summary>The approved explanation projection; source, identity and case coordinates are never output.</summary>
public sealed record ModerationCorrectionExplanation(
    [property: JsonPropertyName("Rule")] string Rule,
    [property: JsonPropertyName("Reason")] string Reason,
    [property: JsonPropertyName("Facts")] ImmutableArray<string> Facts,
    [property: JsonPropertyName("RemainingActions")] string RemainingActions,
    [property: JsonPropertyName("Recourse")] string Recourse,
    [property: JsonPropertyName("Automation")] string Automation,
    [property: JsonPropertyName("Retention")] string Retention)
{
    public override string ToString() => nameof(ModerationCorrectionExplanation);
}
