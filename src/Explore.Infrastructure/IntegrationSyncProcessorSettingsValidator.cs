using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class IntegrationSyncProcessorSettingsValidator : IValidateOptions<IntegrationSyncProcessorSettings>
{
    public ValidateOptionsResult Validate(string? name, IntegrationSyncProcessorSettings options)
    {
        List<string> failures = [];
        if (options.PollingIntervalSeconds is < 1 or > 300)
            failures.Add("IntegrationSyncProcessor:PollingIntervalSeconds must be between 1 and 300.");
        if (options.BatchSize is < 1 or > 1000)
            failures.Add("IntegrationSyncProcessor:BatchSize must be between 1 and 1000.");
        if (options.MaxAttemptCount is < 1 or > 100)
            failures.Add("IntegrationSyncProcessor:MaxAttemptCount must be between 1 and 100.");
        if (options.InitialRetryDelaySeconds is < 1 or > 3600)
            failures.Add("IntegrationSyncProcessor:InitialRetryDelaySeconds must be between 1 and 3600.");
        if (options.MaxRetryDelaySeconds < options.InitialRetryDelaySeconds || options.MaxRetryDelaySeconds > 43200)
            failures.Add("IntegrationSyncProcessor:MaxRetryDelaySeconds must be between InitialRetryDelaySeconds and 43200.");
        if (options.ProcessingLeaseTimeoutSeconds is < 30 or > 3600)
            failures.Add("IntegrationSyncProcessor:ProcessingLeaseTimeoutSeconds must be between 30 and 3600.");
        if (options.HealthDueWarningThreshold is < 1 or > 1_000_000)
            failures.Add("IntegrationSyncProcessor:HealthDueWarningThreshold must be between 1 and 1000000.");
        if (options.HealthStaleWarningThreshold is < 1 or > 100_000)
            failures.Add("IntegrationSyncProcessor:HealthStaleWarningThreshold must be between 1 and 100000.");
        if (options.HealthAmbiguousWarningThreshold is < 1 or > 100_000)
            failures.Add("IntegrationSyncProcessor:HealthAmbiguousWarningThreshold must be between 1 and 100000.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
