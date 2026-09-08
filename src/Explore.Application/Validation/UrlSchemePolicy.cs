namespace Explore.Application.Validation;

public static class UrlSchemePolicy
{
    public static bool IsAllowed(string value, bool requireHttps)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        if (trimmed.StartsWith('/'))
            return !trimmed.StartsWith("//", StringComparison.Ordinal);

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || (!requireHttps && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));
    }
}
