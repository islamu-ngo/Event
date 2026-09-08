using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookRetentionSettings
{
    public const string SectionName = "WebhookRetention";

    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; }
    public int InitialDelaySeconds { get; set; } = 60;
    public int PollingIntervalMinutes { get; set; } = 60;
    public int MaxTenantsPerPass { get; set; } = 100;
    public int BatchSize { get; set; } = 500;
    public int InboundPayloadRetentionDays { get; set; } = 14;
    public int OutboundPayloadRetentionDays { get; set; } = 14;
    public int ProcessingAttemptRetentionDays { get; set; } = 30;
    public int DeadLetterEvidenceRetentionDays { get; set; } = 90;
    public int ProviderPublicationRetentionDays { get; set; } = 90;
    public int OperationalLogRetentionDays { get; set; } = 30;
    public int AdministrativeAuditRetentionDays { get; set; } = 365;
    public int ReplayWindowDays { get; set; } = 14;
}

public sealed class WebhookRetentionSettingsValidator : IValidateOptions<WebhookRetentionSettings>
{
    public ValidateOptionsResult Validate(string? name, WebhookRetentionSettings settings)
    {
        List<string> failures = [];
        ValidateRange(settings.InitialDelaySeconds, 0, 3_600, nameof(settings.InitialDelaySeconds), failures);
        ValidateRange(settings.PollingIntervalMinutes, 1, 10_080, nameof(settings.PollingIntervalMinutes), failures);
        ValidateRange(settings.MaxTenantsPerPass, 1, 10_000, nameof(settings.MaxTenantsPerPass), failures);
        ValidateRange(settings.BatchSize, 1, 10_000, nameof(settings.BatchSize), failures);
        ValidateRange(settings.InboundPayloadRetentionDays, 1, 3_650, nameof(settings.InboundPayloadRetentionDays), failures);
        ValidateRange(settings.OutboundPayloadRetentionDays, 1, 3_650, nameof(settings.OutboundPayloadRetentionDays), failures);
        ValidateRange(settings.ProcessingAttemptRetentionDays, 1, 3_650, nameof(settings.ProcessingAttemptRetentionDays), failures);
        ValidateRange(settings.DeadLetterEvidenceRetentionDays, 1, 3_650, nameof(settings.DeadLetterEvidenceRetentionDays), failures);
        ValidateRange(settings.ProviderPublicationRetentionDays, 1, 3_650, nameof(settings.ProviderPublicationRetentionDays), failures);
        ValidateRange(settings.OperationalLogRetentionDays, 1, 3_650, nameof(settings.OperationalLogRetentionDays), failures);
        ValidateRange(settings.AdministrativeAuditRetentionDays, 1, 3_650, nameof(settings.AdministrativeAuditRetentionDays), failures);
        ValidateRange(settings.ReplayWindowDays, 1, 3_650, nameof(settings.ReplayWindowDays), failures);

        if (settings.InboundPayloadRetentionDays < settings.ReplayWindowDays)
        {
            failures.Add("WebhookRetention:InboundPayloadRetentionDays cannot be shorter than ReplayWindowDays.");
        }

        if (settings.DeadLetterEvidenceRetentionDays < settings.ProcessingAttemptRetentionDays)
        {
            failures.Add("WebhookRetention:DeadLetterEvidenceRetentionDays cannot be shorter than ProcessingAttemptRetentionDays.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string name,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"WebhookRetention:{name} must be between {minimum} and {maximum}.");
        }
    }
}
