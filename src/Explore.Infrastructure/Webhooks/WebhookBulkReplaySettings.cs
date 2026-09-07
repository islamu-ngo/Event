using Explore.Domain;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookBulkReplaySettings
{
    public const string SectionName = "WebhookBulkReplay";

    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 10;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int OperationsPerPass { get; set; } = 10;
    public int MaximumItemsPerOperation { get; set; } = 100;
    public int MaximumReservedItemsPerTenant { get; set; } = 500;
    public int MaximumFilterWindowDays { get; set; } = 30;
    public int HealthQueuedWarningThreshold { get; set; } = 100;
    public int HealthExecutingWarningThreshold { get; set; } = 100;
}

public sealed class WebhookBulkReplaySettingsValidator : IValidateOptions<WebhookBulkReplaySettings>
{
    public ValidateOptionsResult Validate(string? name, WebhookBulkReplaySettings settings)
    {
        List<string> failures = [];
        ValidateRange(settings.InitialDelaySeconds, 0, 3_600, nameof(settings.InitialDelaySeconds), failures);
        ValidateRange(settings.PollingIntervalSeconds, 1, 3_600, nameof(settings.PollingIntervalSeconds), failures);
        ValidateRange(settings.OperationsPerPass, 1, 100, nameof(settings.OperationsPerPass), failures);
        ValidateRange(
            settings.MaximumItemsPerOperation,
            1,
            WebhookBulkReplayOperation.HardMaximumItems,
            nameof(settings.MaximumItemsPerOperation),
            failures);
        ValidateRange(
            settings.MaximumReservedItemsPerTenant,
            1,
            100_000,
            nameof(settings.MaximumReservedItemsPerTenant),
            failures);
        ValidateRange(settings.MaximumFilterWindowDays, 1, 365, nameof(settings.MaximumFilterWindowDays), failures);
        ValidateRange(settings.HealthQueuedWarningThreshold, 1, 100_000, nameof(settings.HealthQueuedWarningThreshold), failures);
        ValidateRange(settings.HealthExecutingWarningThreshold, 1, 100_000, nameof(settings.HealthExecutingWarningThreshold), failures);

        if (settings.MaximumReservedItemsPerTenant < settings.MaximumItemsPerOperation)
        {
            failures.Add(
                "WebhookBulkReplay:MaximumReservedItemsPerTenant cannot be smaller than MaximumItemsPerOperation.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string settingName,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"WebhookBulkReplay:{settingName} must be between {minimum} and {maximum}.");
        }
    }
}
