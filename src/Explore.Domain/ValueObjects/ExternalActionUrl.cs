namespace Explore.Domain.ValueObjects;

public sealed record ExternalActionUrl
{
    private ExternalActionUrl(string value, string destinationDomain)
    {
        Value = value;
        DestinationDomain = destinationDomain;
    }

    public string Value { get; }
    public string DestinationDomain { get; }

    public static ExternalActionUrl Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("External action URL must be an absolute HTTPS URL without userinfo or a fragment.", nameof(value));
        }

        var normalized = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Host = uri.IdnHost.ToLowerInvariant(),
            Port = uri.IsDefaultPort ? -1 : uri.Port
        }.Uri.AbsoluteUri;

        return new ExternalActionUrl(normalized, uri.IdnHost.ToLowerInvariant());
    }
}
