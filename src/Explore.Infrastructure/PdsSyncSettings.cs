using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class PdsSyncSettings
{
    public const string SectionName = "Atproto:PdsSync";

    public bool Enabled { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public int MaxConcurrency { get; set; } = 10;
    public int LeaseDurationSeconds { get; set; } = 90;
    public int HealthDueWarningThreshold { get; set; } = 1000;
    public int HealthStaleWarningThreshold { get; set; } = 1;
    public int HealthDeadLetterWarningThreshold { get; set; } = 1;
}

public sealed class PdsSyncSettingsValidator : IValidateOptions<PdsSyncSettings>
{
    public ValidateOptionsResult Validate(string? name, PdsSyncSettings options)
    {
        List<string> failures = [];
        if (options.PollingIntervalSeconds is < 1 or > 300)
        {
            failures.Add("Atproto:PdsSync:PollingIntervalSeconds must be between 1 and 300.");
        }
        if (options.BatchSize is < 1 or > 100)
        {
            failures.Add("Atproto:PdsSync:BatchSize must be between 1 and 100.");
        }
        if (options.MaxConcurrency < 1 || options.MaxConcurrency > options.BatchSize)
        {
            failures.Add("Atproto:PdsSync:MaxConcurrency must be between 1 and BatchSize.");
        }
        if (options.LeaseDurationSeconds is < 30 or > 900)
        {
            failures.Add("Atproto:PdsSync:LeaseDurationSeconds must be between 30 and 900.");
        }
        if (options.HealthDueWarningThreshold is < 1 or > 1_000_000)
        {
            failures.Add("Atproto:PdsSync:HealthDueWarningThreshold must be between 1 and 1000000.");
        }
        if (options.HealthStaleWarningThreshold is < 1 or > 100_000)
        {
            failures.Add("Atproto:PdsSync:HealthStaleWarningThreshold must be between 1 and 100000.");
        }
        if (options.HealthDeadLetterWarningThreshold is < 1 or > 100_000)
        {
            failures.Add("Atproto:PdsSync:HealthDeadLetterWarningThreshold must be between 1 and 100000.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
