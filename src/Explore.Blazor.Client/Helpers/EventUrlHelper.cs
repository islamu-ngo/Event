using System.Globalization;
using System.Text;

namespace Explore.Blazor.Client.Helpers;

public static class EventUrlHelper
{
    public static string? GetSafeExternalUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.AbsoluteUri
                : null;
    }

    public static string? BuildPublicPath(string? slug, string? publicCode)
    {
        var slugCode = BuildPublicSlugCode(slug, publicCode);
        return slugCode is null ? null : $"/events/{slugCode}";
    }

    public static string? BuildPublicSlugCode(string? slug, string? publicCode)
    {
        if (string.IsNullOrWhiteSpace(publicCode))
            return null;

        var cleanSlug = FormatSlug(slug);
        if (string.IsNullOrWhiteSpace(cleanSlug))
            cleanSlug = "event";

        return $"{cleanSlug}-{publicCode}";
    }

    public static string FormatSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var previousWasDash = false;

        foreach (var character in value.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasDash = false;
                continue;
            }

            if (char.IsWhiteSpace(character) || character == '-')
            {
                if (!previousWasDash && builder.Length > 0)
                    builder.Append('-');

                previousWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
