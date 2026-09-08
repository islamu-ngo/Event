using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Waitlist;

public sealed class
    FairReturnOrchestrationDrainSettingsValidator :
    IValidateOptions<
        FairReturnOrchestrationDrainSettings>
{
    public ValidateOptionsResult Validate(
        string? name,
        FairReturnOrchestrationDrainSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BatchSize is < 1
            or > FairReturnOrchestrationDrainSettings
                .MaximumBatchSize)
        {
            return ValidateOptionsResult.Fail(
                "Fair-return batch size is invalid.");
        }
        if (options.MaximumEffectsPerTenant is < 1
            || options.MaximumEffectsPerTenant >
                options.BatchSize)
        {
            return ValidateOptionsResult.Fail(
                "Fair-return tenant batch limit is invalid.");
        }
        if (options.LeaseDurationSeconds is < 10
            or > 3600)
        {
            return ValidateOptionsResult.Fail(
                "Fair-return lease duration is invalid.");
        }
        return ValidateOptionsResult.Success;
    }
}
