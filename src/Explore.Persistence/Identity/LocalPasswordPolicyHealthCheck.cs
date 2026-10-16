using Explore.Application.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Persistence.Identity;

internal sealed class LocalPasswordPolicyHealthCheck(
    LocalPasswordBlocklistSnapshot snapshot, IOptions<PasswordHasherOptions> options, bool required) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PasswordHasherOptions effective = options.Value;
        bool costValid = effective.CompatibilityMode == PasswordHasherCompatibilityMode.IdentityV3
            && effective.IterationCount is >= LocalIdentityOptions.MinimumHashIterations
                and <= LocalIdentityOptions.MaximumHashIterations;
        var data = new Dictionary<string, object>
        {
            ["algorithm"] = "PBKDF2-HMAC-SHA512",
            ["format"] = "IdentityV3",
            ["iterations"] = effective.IterationCount,
            ["saltBits"] = 128,
            ["subkeyBits"] = 256,
            ["normalization"] = "NFC",
            ["snapshotUsable"] = snapshot.IsUsable,
            ["snapshotFormat"] = 1,
            ["snapshotEntries"] = snapshot.Count
        };
        return Task.FromResult(!costValid || (required && !snapshot.IsUsable)
            ? HealthCheckResult.Unhealthy("Required Local password policy is unavailable.", data: data)
            : HealthCheckResult.Healthy("Local password policy status.", data));
    }
}
