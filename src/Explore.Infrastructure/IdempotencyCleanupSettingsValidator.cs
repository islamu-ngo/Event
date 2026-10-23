using Explore.Application.Contracts.Persistence;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class IdempotencyCleanupSettingsValidator : IValidateOptions<IdempotencyCleanupSettings>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyCleanupSettings options)
    {
        var failures = new List<string>();

        if (options.InitialDelaySeconds < 0)
        {
            failures.Add("IdempotencyCleanup:InitialDelaySeconds must be zero or greater.");
        }

        if (options.PollingIntervalMinutes <= 0)
        {
            failures.Add("IdempotencyCleanup:PollingIntervalMinutes must be greater than zero.");
        }

        if (options.BatchSize is < 1 or > IdempotencyCleanupLimits.MaximumRows)
        {
            failures.Add($"IdempotencyCleanup:BatchSize must be between 1 and {IdempotencyCleanupLimits.MaximumRows}.");
        }

        if (options.ExpirationGraceHours < 0)
        {
            failures.Add("IdempotencyCleanup:ExpirationGraceHours must be zero or greater.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
