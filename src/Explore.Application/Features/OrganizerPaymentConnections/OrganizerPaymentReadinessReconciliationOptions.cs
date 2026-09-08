using Microsoft.Extensions.Options;

namespace Explore.Application.Features.OrganizerPaymentConnections;

public sealed class OrganizerPaymentReadinessReconciliationOptions
{
    public const string SectionName = "OrganizerPaymentReadinessReconciliation";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 25;
    public int StaleIntervalMinutes { get; set; } = 5;
    public int PollingIntervalSeconds { get; set; } = 60;
    public int InitialDelaySeconds { get; set; } = 5;
}

public sealed class OrganizerPaymentReadinessReconciliationOptionsValidator
    : IValidateOptions<OrganizerPaymentReadinessReconciliationOptions>
{
    public ValidateOptionsResult Validate(string? name, OrganizerPaymentReadinessReconciliationOptions options)
    {
        if (options.BatchSize is < 1 or > 100)
        {
            return ValidateOptionsResult.Fail("Organizer payment readiness batch size must be between 1 and 100.");
        }

        if (options.StaleIntervalMinutes is < 1 or > 1440)
        {
            return ValidateOptionsResult.Fail("Organizer payment readiness stale interval must be between 1 and 1440 minutes.");
        }

        if (options.PollingIntervalSeconds is < 5 or > 3600)
        {
            return ValidateOptionsResult.Fail("Organizer payment readiness polling interval must be between 5 and 3600 seconds.");
        }

        return options.InitialDelaySeconds is < 0 or > 300
            ? ValidateOptionsResult.Fail("Organizer payment readiness initial delay must be between 0 and 300 seconds.")
            : ValidateOptionsResult.Success;
    }
}
