using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class EmailDispatchProcessorSettingsValidator : IValidateOptions<EmailDispatchProcessorSettings>
{
    private const int MaximumBatchSize = 1000;
    private const int MaximumConcurrentDispatches = 256;
    private const int MaximumSmtpRatePerMinute = 100000;
    private const int MaximumOptionalBacklogWatermark = 1000000;
    private const int MaximumHealthOldestPendingWarningSeconds = 604800;
    private const int MaximumHealthTenantBacklogWarningThreshold = 100000;

    public ValidateOptionsResult Validate(string? name, EmailDispatchProcessorSettings options)
    {
        var failures = new List<string>();

        if (!Enum.IsDefined(options.Mode))
        {
            failures.Add("EmailDispatchProcessor:Mode must be Disabled, Quartz, or HostedService.");
        }

        if (options.PollingIntervalSeconds <= 0)
        {
            failures.Add("EmailDispatchProcessor:PollingIntervalSeconds must be greater than zero.");
        }

        if (options.BatchSize is < 1 or > MaximumBatchSize)
        {
            failures.Add("EmailDispatchProcessor:BatchSize must be between 1 and 1000.");
        }

        if (options.MaxRowsPerTenantPerBatch is < 1 or > MaximumBatchSize ||
            options.MaxRowsPerTenantPerBatch > options.BatchSize)
        {
            failures.Add("EmailDispatchProcessor:MaxRowsPerTenantPerBatch must be between 1 and BatchSize, up to 1000.");
        }

        if (options.MaxConcurrentDispatches is < 1 or > MaximumConcurrentDispatches)
        {
            failures.Add("EmailDispatchProcessor:MaxConcurrentDispatches must be between 1 and 256.");
        }

        if (options.MaxConcurrentDispatchesPerTenant is < 1 or > MaximumConcurrentDispatches ||
            options.MaxConcurrentDispatchesPerTenant > options.MaxConcurrentDispatches)
        {
            failures.Add("EmailDispatchProcessor:MaxConcurrentDispatchesPerTenant must be between 1 and MaxConcurrentDispatches, up to 256.");
        }

        if (options.GlobalSmtpRateLimitPerMinute is < 1 or > MaximumSmtpRatePerMinute)
        {
            failures.Add("EmailDispatchProcessor:GlobalSmtpRateLimitPerMinute must be between 1 and 100000.");
        }

        if (options.TenantSmtpRateLimitPerMinute is < 1 or > MaximumSmtpRatePerMinute
            || options.TenantSmtpRateLimitPerMinute > options.GlobalSmtpRateLimitPerMinute)
        {
            failures.Add("EmailDispatchProcessor:TenantSmtpRateLimitPerMinute must be between 1 and GlobalSmtpRateLimitPerMinute, up to 100000.");
        }

        if (options.OptionalBacklogHighWatermark is < 1 or > MaximumOptionalBacklogWatermark)
        {
            failures.Add("EmailDispatchProcessor:OptionalBacklogHighWatermark must be between 1 and 1000000.");
        }

        if (options.OptionalBacklogLowWatermark is < 0 or > MaximumOptionalBacklogWatermark)
        {
            failures.Add("EmailDispatchProcessor:OptionalBacklogLowWatermark must be between 0 and 1000000.");
        }

        if (options.OptionalBacklogHighWatermark <= options.OptionalBacklogLowWatermark)
        {
            failures.Add("EmailDispatchProcessor:OptionalBacklogLowWatermark must be lower than OptionalBacklogHighWatermark.");
        }

        if (options.MaxAttemptCount <= 0)
        {
            failures.Add("EmailDispatchProcessor:MaxAttemptCount must be greater than zero.");
        }

        if (options.InitialRetryDelaySeconds <= 0)
        {
            failures.Add("EmailDispatchProcessor:InitialRetryDelaySeconds must be greater than zero.");
        }

        if (options.MaxRetryDelaySeconds < options.InitialRetryDelaySeconds)
        {
            failures.Add("EmailDispatchProcessor:MaxRetryDelaySeconds must be greater than or equal to InitialRetryDelaySeconds.");
        }

        if (options.ProcessingLeaseTimeoutSeconds <= 0)
        {
            failures.Add("EmailDispatchProcessor:ProcessingLeaseTimeoutSeconds must be greater than zero.");
        }

        if (options.HealthDueDispatchWarningThreshold is < 1 or > 100000)
        {
            failures.Add("EmailDispatchProcessor:HealthDueDispatchWarningThreshold must be between 1 and 100000.");
        }

        if (options.HealthStaleProcessingWarningThreshold is < 1 or > 10000)
        {
            failures.Add("EmailDispatchProcessor:HealthStaleProcessingWarningThreshold must be between 1 and 10000.");
        }

        if (options.HealthUnknownWarningThreshold is < 1 or > 10000)
        {
            failures.Add("EmailDispatchProcessor:HealthUnknownWarningThreshold must be between 1 and 10000.");
        }

        if (options.HealthDeadLetterWarningThreshold is < 1 or > 10000)
        {
            failures.Add("EmailDispatchProcessor:HealthDeadLetterWarningThreshold must be between 1 and 10000.");
        }

        if (options.HealthOldestPendingWarningSeconds is < 1 or > MaximumHealthOldestPendingWarningSeconds)
        {
            failures.Add("EmailDispatchProcessor:HealthOldestPendingWarningSeconds must be between 1 and 604800.");
        }

        if (options.HealthTenantBacklogWarningThreshold is < 1 or > MaximumHealthTenantBacklogWarningThreshold)
        {
            failures.Add("EmailDispatchProcessor:HealthTenantBacklogWarningThreshold must be between 1 and 100000.");
        }

        if (options.HealthTenantSampleLimit is < 1 or > 100)
        {
            failures.Add("EmailDispatchProcessor:HealthTenantSampleLimit must be between 1 and 100.");
        }

        if (string.IsNullOrWhiteSpace(options.ConsumerId))
        {
            failures.Add("EmailDispatchProcessor:ConsumerId is required when Basic Dispatch Mode is configured.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
