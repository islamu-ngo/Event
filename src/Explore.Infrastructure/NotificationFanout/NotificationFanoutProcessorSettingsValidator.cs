using Explore.Application.Services;
using Explore.Domain;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.NotificationFanout;

public sealed class NotificationFanoutProcessorSettingsValidator
    : IValidateOptions<NotificationFanoutProcessorSettings>
{
    private const int MaximumClaims = 256;
    private const int MaximumBacklogWatermark = 1_000_000;

    public ValidateOptionsResult Validate(string? name, NotificationFanoutProcessorSettings options)
    {
        var failures = new List<string>();

        if (options.PollingIntervalSeconds is < 1 or > 300)
        {
            failures.Add("NotificationFanoutProcessor:PollingIntervalSeconds must be between 1 and 300.");
        }

        if (options.PageSize is < 1 or > NotificationFanoutPageProcessor.MaxPageSize)
        {
            failures.Add($"NotificationFanoutProcessor:PageSize must be between 1 and {NotificationFanoutPageProcessor.MaxPageSize}.");
        }

        if (options.MaxClaimsPerRound is < 1 or > MaximumClaims
            || options.MaxClaimsPerRound > options.MaxActiveClaims)
        {
            failures.Add("NotificationFanoutProcessor:MaxClaimsPerRound must be between 1 and MaxActiveClaims, up to 256.");
        }

        if (options.MaxActiveClaims is < 1 or > MaximumClaims)
        {
            failures.Add("NotificationFanoutProcessor:MaxActiveClaims must be between 1 and 256.");
        }

        if (options.MaxActiveClaimsPerTenant is < 1 or > MaximumClaims
            || options.MaxActiveClaimsPerTenant > options.MaxActiveClaims)
        {
            failures.Add("NotificationFanoutProcessor:MaxActiveClaimsPerTenant must be between 1 and MaxActiveClaims, up to 256.");
        }

        if (options.ClaimLeaseSeconds is < 30 or > 3600)
        {
            failures.Add("NotificationFanoutProcessor:ClaimLeaseSeconds must be between 30 and 3600.");
        }

        if (options.OptionalReminderBacklogHighWatermark is < 1 or > MaximumBacklogWatermark)
        {
            failures.Add("NotificationFanoutProcessor:OptionalReminderBacklogHighWatermark must be between 1 and 1000000.");
        }

        if (options.OptionalReminderBacklogLowWatermark is < 0 or > MaximumBacklogWatermark
            || options.OptionalReminderBacklogLowWatermark >= options.OptionalReminderBacklogHighWatermark)
        {
            failures.Add("NotificationFanoutProcessor:OptionalReminderBacklogLowWatermark must be lower than the high watermark.");
        }

        if (options.HealthDueOccurrenceWarningThreshold is < 1 or > MaximumBacklogWatermark)
        {
            failures.Add("NotificationFanoutProcessor:HealthDueOccurrenceWarningThreshold must be between 1 and 1000000.");
        }

        if (options.HealthExpiredClaimWarningThreshold is < 1 or > 10000)
        {
            failures.Add("NotificationFanoutProcessor:HealthExpiredClaimWarningThreshold must be between 1 and 10000.");
        }

        if (options.HealthOldestDueWarningSeconds is < 1 or > 604800)
        {
            failures.Add("NotificationFanoutProcessor:HealthOldestDueWarningSeconds must be between 1 and 604800.");
        }

        if (string.IsNullOrWhiteSpace(options.ConsumerId)
            || options.ConsumerId.Trim().Length > NotificationFanoutRun.MaxLeaseOwnerLength)
        {
            failures.Add($"NotificationFanoutProcessor:ConsumerId is required and must not exceed {NotificationFanoutRun.MaxLeaseOwnerLength} characters.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
