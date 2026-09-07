namespace Explore.Diagnostic.AiReplay;

public sealed record AiReplayReport(
    DateTimeOffset GeneratedAtUtc,
    string ReportVersion,
    bool UsesLiveProviderCredentials,
    bool ContainsContentBearingArtifacts,
    IReadOnlyList<AiReplayScenarioResult> Results)
{
    public int PassCount => Results.Count(result => result.Status == AiReplayScenarioStatus.Pass);
    public int WarnCount => Results.Count(result => result.Status == AiReplayScenarioStatus.Warn);
    public int FailCount => Results.Count(result => result.Status == AiReplayScenarioStatus.Fail);
    public decimal PassRate => Results.Count == 0 ? 0m : decimal.Round((decimal)PassCount / Results.Count, 4);
    public bool HasDatabaseSideEffects => Results.Any(result => result.DatabaseSideEffectsDetected);
    public bool IsCiSafe => FailCount == 0 &&
                            !UsesLiveProviderCredentials &&
                            !ContainsContentBearingArtifacts &&
                            !HasDatabaseSideEffects;
}
