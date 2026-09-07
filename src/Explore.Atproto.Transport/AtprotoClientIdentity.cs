using System.Net;

namespace Explore.Atproto.Transport;

public sealed record AtprotoClientIdentity(string ClientId, string CallbackUri, Uri PublicOrigin);

public static class AtprotoClientIdentityFactory
{
    public static bool TryCreate(
        string? configuredPublicUrl,
        string? configuredCallbackPath,
        AtprotoOutboundPolicy policy,
        out AtprotoClientIdentity identity)
    {
        identity = null!;
        if (string.IsNullOrWhiteSpace(configuredPublicUrl)
            || !string.Equals(configuredPublicUrl, configuredPublicUrl.Trim(), StringComparison.Ordinal)
            || !Uri.TryCreate(configuredPublicUrl, UriKind.Absolute, out var publicUri)
            || publicUri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(publicUri.UserInfo)
            || !string.IsNullOrEmpty(publicUri.Query)
            || !string.IsNullOrEmpty(publicUri.Fragment)
            || Uri.CheckHostName(publicUri.Host) is UriHostNameType.Unknown
            || !TryCanonicalCallbackPath(configuredCallbackPath, out var callbackPath))
        {
            return false;
        }

        try
        {
            policy.ValidateUri(publicUri);
        }
        catch (AtprotoOAuthSecurityException)
        {
            return false;
        }

        var origin = new UriBuilder(publicUri.Scheme, publicUri.IdnHost, publicUri.IsDefaultPort ? -1 : publicUri.Port, "/").Uri;
        identity = new(
            new Uri(origin, "oauth/client-metadata.json").AbsoluteUri,
            new Uri(origin, callbackPath.TrimStart('/')).AbsoluteUri,
            origin);
        return true;
    }

    private static bool TryCanonicalCallbackPath(string? configuredPath, out string callbackPath)
    {
        callbackPath = "/signin-atproto";
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return true;
        }

        if (configuredPath.Length is < 2 or > 256
            || configuredPath[0] != '/'
            || configuredPath[1] == '/'
            || configuredPath.Contains("//", StringComparison.Ordinal)
            || configuredPath.Any(character => !IsCanonicalPathCharacter(character))
            || configuredPath.Split('/', StringSplitOptions.None).Any(segment => segment is "." or ".."))
        {
            return false;
        }

        callbackPath = configuredPath;
        return true;
    }

    private static bool IsCanonicalPathCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '/' or '-' or '_' or '.' or '~';
}
