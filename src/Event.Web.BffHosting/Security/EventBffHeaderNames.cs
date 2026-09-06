// ABOUTME: Centralizes privileged browser-BFF header names owned by the hosting boundary.
// ABOUTME: Keeps proxy sanitization independent from Application/API project references.

namespace Event.Web.BffHosting.Security;

public static class EventBffHeaderNames
{
    public const string ApiKey = "X-API-Key";
    public const string TenantId = "X-Tenant-Id";
    public const string TenantSlug = "X-Tenant-Slug";
    public const string SetupSecret = "X-Setup-Secret";
    public const string AtprotoBootstrapAssertion = "X-Atproto-Bootstrap-Assertion";
    public const string AtprotoSessionBridgeAssertion = "X-Atproto-Session-Bridge-Assertion";
    public const string AtprotoTransientAssertion = "X-Atproto-Transient-Assertion";
    public const string SupportAccessSessionId = "X-Support-Access-Session-Id";
    public const string SupportAccessTargetTenantId = "X-Support-Access-Target-Tenant-Id";
    public const string SupportAccessMode = "X-Support-Access-Mode";
    public const string SupportAccessPrefix = "X-Support-Access-";

    public static bool IsSupportAccessHeader(string headerName)
    {
        return !string.IsNullOrWhiteSpace(headerName)
            && (headerName.Equals(SupportAccessSessionId, StringComparison.OrdinalIgnoreCase)
                || headerName.Equals(SupportAccessTargetTenantId, StringComparison.OrdinalIgnoreCase)
                || headerName.Equals(SupportAccessMode, StringComparison.OrdinalIgnoreCase)
                || headerName.StartsWith(SupportAccessPrefix, StringComparison.OrdinalIgnoreCase));
    }
}
