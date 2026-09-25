using Explore.Domain.ValueObjects;
using System.Globalization;

namespace Explore.Application.Validation;

public static class EventResourceDestinationValidator
{
    public static bool TryValidate(string? input, EventResourceGovernancePolicy policy,
        out string? destination, out string? safeOrigin)
    {
        destination = null;
        safeOrigin = null;
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrEmpty(input) || input.Length > 4096
            || input.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)
                || character == '\\' || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format)
            || !Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || !uri.IsWellFormedOriginalString()
            || uri.Scheme != Uri.UriSchemeHttps || uri.HostNameType != UriHostNameType.Dns
            || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        string origin = uri.GetLeftPart(UriPartial.Authority);
        if (!policy.AllowsExternalOrigin(origin) || uri.AbsoluteUri.Length > 4096)
            return false;

        destination = uri.AbsoluteUri;
        safeOrigin = origin;
        return true;
    }
}
