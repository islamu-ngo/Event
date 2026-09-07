using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookProviderPublicationProcessorSettings
{
    public const string SectionName = "WebhookProviderPublicationProcessor";

    public bool Enabled { get; set; }
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
    public int LeaseSeconds { get; set; } = 120;
    public int MaxAutomaticPublicationAttempts { get; set; } = 8;
    public int MaxAutomaticReconciliationAttempts { get; set; } = 4;
    public int InitialRetryDelaySeconds { get; set; } = 30;
    public int MaxRetryDelaySeconds { get; set; } = 3600;
    public int UnknownReconciliationDelaySeconds { get; set; } = 30;
    public int ReconciliationRetryDelaySeconds { get; set; } = 30;
    public int ReconciliationLookupPageLimit { get; set; } = 100;
    public int HealthDueWarningThreshold { get; set; } = 1000;
    public int HealthStaleWarningThreshold { get; set; } = 1;
    public int HealthUnknownWarningThreshold { get; set; } = 1;
}

public sealed class WebhookProviderPublicationProcessorSettingsValidator
    : IValidateOptions<WebhookProviderPublicationProcessorSettings>
{
    public ValidateOptionsResult Validate(
        string? name,
        WebhookProviderPublicationProcessorSettings options)
    {
        List<string> failures = [];

        if (options.PollingIntervalSeconds is < 1 or > 300)
        {
            failures.Add("WebhookProviderPublicationProcessor:PollingIntervalSeconds must be between 1 and 300.");
        }

        if (options.BatchSize is < 1 or > 1000)
        {
            failures.Add("WebhookProviderPublicationProcessor:BatchSize must be between 1 and 1000.");
        }

        if (options.LeaseSeconds is < 30 or > 3600)
        {
            failures.Add("WebhookProviderPublicationProcessor:LeaseSeconds must be between 30 and 3600.");
        }

        if (options.MaxAutomaticPublicationAttempts is < 1 or > 100)
        {
            failures.Add("WebhookProviderPublicationProcessor:MaxAutomaticPublicationAttempts must be between 1 and 100.");
        }

        if (options.MaxAutomaticReconciliationAttempts is < 1 or > 100)
        {
            failures.Add("WebhookProviderPublicationProcessor:MaxAutomaticReconciliationAttempts must be between 1 and 100.");
        }

        if (options.InitialRetryDelaySeconds is < 1 or > 3600)
        {
            failures.Add("WebhookProviderPublicationProcessor:InitialRetryDelaySeconds must be between 1 and 3600.");
        }

        if (options.MaxRetryDelaySeconds < options.InitialRetryDelaySeconds ||
            options.MaxRetryDelaySeconds > 43_200)
        {
            failures.Add("WebhookProviderPublicationProcessor:MaxRetryDelaySeconds must be between InitialRetryDelaySeconds and 43200.");
        }

        if (options.UnknownReconciliationDelaySeconds is < 1 or > 3600)
        {
            failures.Add("WebhookProviderPublicationProcessor:UnknownReconciliationDelaySeconds must be between 1 and 3600.");
        }


        if (options.ReconciliationRetryDelaySeconds is < 1 or > 3600)
        {
            failures.Add("WebhookProviderPublicationProcessor:ReconciliationRetryDelaySeconds must be between 1 and 3600.");
        }

        if (options.ReconciliationLookupPageLimit is < 1 or > 1000)
        {
            failures.Add("WebhookProviderPublicationProcessor:ReconciliationLookupPageLimit must be between 1 and 1000.");
        }
        if (options.HealthDueWarningThreshold is < 1 or > 1_000_000)
        {
            failures.Add("WebhookProviderPublicationProcessor:HealthDueWarningThreshold must be between 1 and 1000000.");
        }
        if (options.HealthStaleWarningThreshold is < 1 or > 100_000)
        {
            failures.Add("WebhookProviderPublicationProcessor:HealthStaleWarningThreshold must be between 1 and 100000.");
        }
        if (options.HealthUnknownWarningThreshold is < 1 or > 100_000)
        {
            failures.Add("WebhookProviderPublicationProcessor:HealthUnknownWarningThreshold must be between 1 and 100000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
