using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookDeliveryProcessorSettings
{
    public const string SectionName = "WebhookDeliveryProcessor";

    public bool Enabled { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int InitialDelaySeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
    public int CandidateBatchSize { get; set; } = 1000;
    public int MaxConcurrentDeliveries { get; set; } = 16;
    public int MaxConcurrentDeliveriesPerTenant { get; set; } = 4;
    public int MaxConcurrentDeliveriesPerEndpoint { get; set; } = 1;
    public int MaxItemsPerTenantPerClaimCycle { get; set; } = 10;
    public int ProcessingLeaseTimeoutSeconds { get; set; } = 120;
    public int HealthDueAttemptWarningThreshold { get; set; } = 1000;
    public int HealthStaleSendingWarningThreshold { get; set; } = 1;
    public bool VerboseLogging { get; set; }
}

public sealed class WebhookDeliveryProcessorSettingsValidator : IValidateOptions<WebhookDeliveryProcessorSettings>
{
    public ValidateOptionsResult Validate(string? name, WebhookDeliveryProcessorSettings options)
    {
        List<string> failures = [];

        if (options.PollingIntervalSeconds is < 1 or > 300)
        {
            failures.Add("WebhookDeliveryProcessor:PollingIntervalSeconds must be between 1 and 300.");
        }

        if (options.InitialDelaySeconds is < 0 or > 300)
        {
            failures.Add("WebhookDeliveryProcessor:InitialDelaySeconds must be between 0 and 300.");
        }

        if (options.BatchSize is < 1 or > 1000)
        {
            failures.Add("WebhookDeliveryProcessor:BatchSize must be between 1 and 1000.");
        }

        if (options.CandidateBatchSize is < 1 or > 10000)
        {
            failures.Add("WebhookDeliveryProcessor:CandidateBatchSize must be between 1 and 10000.");
        }

        if (options.CandidateBatchSize < options.BatchSize)
        {
            failures.Add("WebhookDeliveryProcessor:CandidateBatchSize cannot be less than WebhookDeliveryProcessor:BatchSize.");
        }

        if (options.MaxConcurrentDeliveries is < 1 or > 256)
        {
            failures.Add("WebhookDeliveryProcessor:MaxConcurrentDeliveries must be between 1 and 256.");
        }

        if (options.MaxConcurrentDeliveriesPerTenant is < 1 or > 256
            || options.MaxConcurrentDeliveriesPerTenant > options.MaxConcurrentDeliveries)
        {
            failures.Add("WebhookDeliveryProcessor:MaxConcurrentDeliveriesPerTenant must be between 1 and MaxConcurrentDeliveries.");
        }

        if (options.MaxConcurrentDeliveriesPerEndpoint is < 1 or > 256
            || options.MaxConcurrentDeliveriesPerEndpoint > options.MaxConcurrentDeliveriesPerTenant)
        {
            failures.Add("WebhookDeliveryProcessor:MaxConcurrentDeliveriesPerEndpoint must be between 1 and MaxConcurrentDeliveriesPerTenant.");
        }

        if (options.MaxItemsPerTenantPerClaimCycle is < 1 or > 1000)
        {
            failures.Add("WebhookDeliveryProcessor:MaxItemsPerTenantPerClaimCycle must be between 1 and 1000.");
        }

        if (options.ProcessingLeaseTimeoutSeconds is < 30 or > 3600)
        {
            failures.Add("WebhookDeliveryProcessor:ProcessingLeaseTimeoutSeconds must be between 30 and 3600.");
        }

        if (options.HealthDueAttemptWarningThreshold is < 1 or > 100000)
        {
            failures.Add("WebhookDeliveryProcessor:HealthDueAttemptWarningThreshold must be between 1 and 100000.");
        }

        if (options.HealthStaleSendingWarningThreshold is < 1 or > 10000)
        {
            failures.Add("WebhookDeliveryProcessor:HealthStaleSendingWarningThreshold must be between 1 and 10000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
