using Event.Web.BffHosting.Abstractions;
using Event.Web.BffHosting.Options;
using Microsoft.Extensions.Options;

namespace Event.Web.BffHosting.Security;

public sealed class EventBffHostClassifier : IEventBffHostClassifier
{
    private readonly HashSet<string> _adminHosts;

    public EventBffHostClassifier(IOptions<EventBffHostingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _adminHosts = options.Value.AdminHosts
            .Select(NormalizeHost)
            .Where(static host => host is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }

    public bool IsAdminHost(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return IsAdminHost(httpContext.Request.Host.Host);
    }

    public bool IsAdminHost(string? host)
    {
        var normalizedHost = NormalizeHost(host);

        return normalizedHost is not null && _adminHosts.Contains(normalizedHost);
    }

    public static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            candidate = uri.Host;
        }
        else
        {
            candidate = candidate
                .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        var portStart = candidate.IndexOf(':', StringComparison.Ordinal);
        if (portStart >= 0)
        {
            candidate = candidate[..portStart];
        }

        candidate = candidate.Trim().Trim('/').TrimEnd('.').ToLowerInvariant();

        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }
}
