using Microsoft.Extensions.Options;

namespace Explore.Application.Configuration;

public sealed class ModerationReconciliationOptionsValidator : IValidateOptions<ModerationReconciliationOptions>
{
    public ValidateOptionsResult Validate(string? name, ModerationReconciliationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled) return ValidateOptionsResult.Success;
        var failures = new List<string>();
        if (options.TenantId == Guid.Empty)
            failures.Add("Moderation reconciliation requires an explicit execution tenant.");
        if (options.BatchSize is < 1 or > 1000)
            failures.Add("Moderation reconciliation batch size must be between 1 and 1000.");
        if (options.IntervalSeconds is < 1 or > 86400 || options.InitialDelaySeconds is < 0 or > 86400)
            failures.Add("Moderation reconciliation cadence must be positive and at most one day; initial delay must be within one day.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
