namespace Explore.Diagnostic.AiReplay;

public static class AiReplayArtifactSafetyPolicy
{
    private static readonly string[] ForbiddenArtifactMarkers =
    [
        "Replay fixture",
        "Community",
        "privateAttendeeNotes",
        "OPENAI_API_KEY",
        "gpt-4",
        "<tool>",
        "018e4e5c"
    ];

    public static bool ContainsContentBearingData(AiReplayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return report.Results.Any(ContainsContentBearingData);
    }

    public static bool ContainsContentBearingData(AiReplayScenarioResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return ContainsContentBearingData(result.Summary) ||
               ContainsContentBearingData(result.Diagnostics);
    }

    public static bool ContainsContentBearingData(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           ForbiddenArtifactMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
