namespace ISLAMU.Wire.Contracts.Identity;

public static class OidcIssuerAuthority
{
    public static string Normalize(string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        if (!Uri.TryCreate(issuer.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("OIDC issuer authority is invalid.", nameof(issuer));
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Path = uri.AbsolutePath.TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty
        };
        if (uri.IsDefaultPort)
            builder.Port = -1;
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }
}
