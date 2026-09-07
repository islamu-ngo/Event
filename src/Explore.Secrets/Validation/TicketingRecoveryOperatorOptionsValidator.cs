using Explore.Domain.Secrets;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Validation;

public sealed class TicketingRecoveryOperatorOptionsValidator :
    IValidateOptions<TicketingRecoveryOperatorOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        TicketingRecoveryOperatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ExpectedReleaseRevision) ||
            options.ExpectedReleaseRevision.Trim().Length > 100)
        {
            errors.Add("Ticketing recovery requires a bounded expected release revision.");
        }
        if (string.IsNullOrWhiteSpace(options.ExpectedSchemaRevision) ||
            options.ExpectedSchemaRevision.Trim().Length > 100)
        {
            errors.Add("Ticketing recovery requires a bounded expected schema revision.");
        }
        if (options.MinimumRetainedKeyVersion <= 0)
        {
            errors.Add("Ticketing recovery retained key version must be positive.");
        }
        if (options.MinimumAuthorityFloor < 0 ||
            options.MinimumProviderCursor < 0 ||
            options.MinimumIdempotencyFloor < 0 ||
            options.MinimumWorkerFence < 0)
        {
            errors.Add("Ticketing recovery floors and fences cannot be negative.");
        }
        if (!string.Equals(
                options.ManifestSigningKeyReference?.Trim(),
                SecretDefinitionRegistry.Keys.Ticketing.RecoveryManifestHmacKey,
                StringComparison.Ordinal))
        {
            errors.Add("Ticketing recovery manifest signing key reference is invalid.");
        }
        if (options.RetainedKeyVersions.Count == 0 ||
            options.RetainedKeyVersions.Any(version => version <= 0) ||
            options.RetainedKeyVersions.Distinct().Count() !=
            options.RetainedKeyVersions.Count ||
            !options.RetainedKeyVersions.Contains(options.MinimumRetainedKeyVersion))
        {
            errors.Add("Ticketing recovery retained key versions must be unique, positive, and include the minimum.");
        }
        if (options.WarningOldestDueSeconds is < 1 or > 3600 ||
            options.UnhealthyOldestDueSeconds <= options.WarningOldestDueSeconds ||
            options.UnhealthyOldestDueSeconds > 7200)
        {
            errors.Add("Ticketing recovery health age thresholds are invalid.");
        }
        if (options.BacklogThreshold is < 1 or > 1_000_000)
        {
            errors.Add("Ticketing recovery backlog threshold is invalid.");
        }
        if (options.DeclaredRpoMinutes is < 1 or > 15 ||
            options.DeclaredRtoMinutes is < 1 or > 60)
        {
            errors.Add("Ticketing recovery declarations must meet RPO <=15 and RTO <=60 minutes.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
