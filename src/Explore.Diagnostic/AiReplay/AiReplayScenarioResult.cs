namespace Explore.Diagnostic.AiReplay;

public sealed record AiReplayScenarioResult(
    string Code,
    AiReplayScenarioStatus Status,
    AiReplayFailureClass FailureClass,
    string Summary,
    string Diagnostics,
    bool UsedLiveProviderCredentials,
    bool DatabaseSideEffectsDetected)
{
    public static AiReplayScenarioResult Pass(
        string code,
        string summary,
        string diagnostics)
        => Create(
            code,
            AiReplayScenarioStatus.Pass,
            AiReplayFailureClass.None,
            summary,
            diagnostics,
            usedLiveProviderCredentials: false,
            databaseSideEffectsDetected: false);

    public static AiReplayScenarioResult Fail(
        string code,
        AiReplayFailureClass failureClass,
        string summary,
        string diagnostics)
        => Create(
            code,
            AiReplayScenarioStatus.Fail,
            failureClass,
            summary,
            diagnostics,
            usedLiveProviderCredentials: false,
            databaseSideEffectsDetected: false);

    private static AiReplayScenarioResult Create(
        string code,
        AiReplayScenarioStatus status,
        AiReplayFailureClass failureClass,
        string summary,
        string diagnostics,
        bool usedLiveProviderCredentials,
        bool databaseSideEffectsDetected)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("AI replay scenario code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("AI replay scenario summary is required.", nameof(summary));
        }

        if (string.IsNullOrWhiteSpace(diagnostics))
        {
            throw new ArgumentException("AI replay scenario diagnostics are required.", nameof(diagnostics));
        }

        return new AiReplayScenarioResult(
            code.Trim(),
            status,
            failureClass,
            summary.Trim(),
            diagnostics.Trim(),
            usedLiveProviderCredentials,
            databaseSideEffectsDetected);
    }
}
