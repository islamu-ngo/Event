using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class StorageReconciliationSettingsValidator : IValidateOptions<StorageReconciliationSettings>
{
    public ValidateOptionsResult Validate(string? name, StorageReconciliationSettings options)
    {
        var failures = new List<string>();

        if (options.InitialDelaySeconds < 0)
        {
            failures.Add("StorageReconciliation:InitialDelaySeconds must be zero or greater.");
        }

        if (options.PollingIntervalMinutes <= 0)
        {
            failures.Add("StorageReconciliation:PollingIntervalMinutes must be greater than zero.");
        }

        if (options.BatchSize <= 0)
        {
            failures.Add("StorageReconciliation:BatchSize must be greater than zero.");
        }

        if (options.MissingObjectQuarantineGraceHours < 0)
        {
            failures.Add("StorageReconciliation:MissingObjectQuarantineGraceHours must be zero or greater.");
        }

        if (options.OrphanFileQuarantineGraceHours < 0)
        {
            failures.Add("StorageReconciliation:OrphanFileQuarantineGraceHours must be zero or greater.");
        }

        if (options.DeleteGraceHours < 0)
        {
            failures.Add("StorageReconciliation:DeleteGraceHours must be zero or greater.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
