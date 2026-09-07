using Event.Web.BffHosting.Security;
using Microsoft.Extensions.Options;

namespace Event.Web.BffHosting.Options;

public sealed class EventBffHostingOptionsValidator : IValidateOptions<EventBffHostingOptions>
{
    public ValidateOptionsResult Validate(string? name, EventBffHostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var normalizedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuredHost in options.AdminHosts)
        {
            var normalizedHost = EventBffHostClassifier.NormalizeHost(configuredHost);
            if (normalizedHost is null)
            {
                failures.Add("Bff:AdminHosts entries must be non-empty host names or origins.");
                continue;
            }

            if (configuredHost.Contains('*', StringComparison.Ordinal))
            {
                failures.Add($"Bff:AdminHosts entry '{configuredHost}' must be an exact host, not a wildcard.");
            }

            if (!normalizedHosts.Add(normalizedHost))
            {
                failures.Add($"Bff:AdminHosts contains duplicate host '{normalizedHost}'.");
            }
        }

        foreach (var configuredRange in options.AdminHostAllowedIpRanges)
        {
            if (!EventBffAdminHostAccessPolicy.TryParseAllowedRange(configuredRange, out _))
            {
                failures.Add($"Bff:AdminHostAllowedIpRanges entry '{configuredRange}' must be an IP address or CIDR range.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
