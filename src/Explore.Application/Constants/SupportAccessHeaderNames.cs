namespace Explore.Application.Constants;

public static class SupportAccessHeaderNames
{
    public const string SessionId = "X-Support-Access-Session-Id";
    public const string TargetTenantId = "X-Support-Access-Target-Tenant-Id";
    public const string Mode = "X-Support-Access-Mode";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SessionId,
        TargetTenantId,
        Mode
    };

    public const string Prefix = "X-Support-Access-";

    public static bool IsSupportAccessHeader(string headerName)
    {
        return !string.IsNullOrWhiteSpace(headerName)
            && (All.Contains(headerName)
                || headerName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase));
    }
}
