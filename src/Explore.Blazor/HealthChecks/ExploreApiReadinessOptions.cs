namespace Explore.Blazor.HealthChecks;

public sealed class ExploreApiReadinessOptions
{
    public const string SectionName = "ExploreApi:Readiness";

    /// <summary>
    /// Total duration to wait for Explore API to become ready during startup before failing.
    /// Default is 60 seconds.
    /// </summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Delay between retry attempts when the API is unreachable.
    /// Default is 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum duration for a single readiness probe HTTP attempt.
    /// Default is 10 seconds.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
