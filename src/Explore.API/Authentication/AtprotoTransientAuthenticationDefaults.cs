// ABOUTME: Defines the purpose-separated transient machine transport and its exact private route set.
// ABOUTME: Route aliases are guarded but only canonical POST paths can authenticate.

namespace Explore.API.Authentication;

public static class AtprotoTransientAuthenticationDefaults
{
    public const string Scheme = "AtprotoTransient";
    public const string HeaderName = "X-Atproto-Transient-Assertion";
    public const string Issuer = "event-atproto-transient-bff";
    public const string Audience = "event-atproto-transient-api";
    public const string Subject = "event-blazor-bff";
    public const string Use = "atproto-transient";
    public const string Prefix = "/api/auth/atproto/transient/";
    public const string RatePolicy = "AtprotoTransient";
    public const int MaximumBodyBytes = 80 * 1024;
    public const int MaximumAssertionBytes = 4096;
    public const int LifetimeSeconds = 30;
    public const int SkewSeconds = 5;
    internal const string BufferedBodyKey = "__atproto_transient_body";
    internal const string VerifiedPurposeKey = "__atproto_transient_purpose";
    private static readonly string[] Operations = ["create", "read", "consume", "probe"];

    public static bool IsPrivatePath(PathString path) =>
        Operations.Any(operation =>
            string.Equals(path.Value?.TrimEnd('/'), Prefix + operation, StringComparison.OrdinalIgnoreCase));

    internal static string? Operation(HttpRequest request) => request.Method == "POST"
        ? request.Path.Value switch
        {
            Prefix + "create" => "create",
            Prefix + "read" => "read",
            Prefix + "consume" => "consume",
            Prefix + "probe" => "probe",
            _ => null
        } : null;
}
