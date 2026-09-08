namespace Explore.API.BackgroundServices;

/// <summary>
/// Controls zero-touch authorization provider reconciliation at API startup.
/// </summary>
public sealed class CerbosPolicyBootSyncOptions
{
    public const string SectionName = "Cerbos:PolicyBootSync";

    /// <summary>
    /// Delay before the background worker starts reconciliation.
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 5;

    public int RetryDelaySeconds { get; set; } = 3;

    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Maximum time allowed for each reconciliation attempt.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}
