using System.Text.Json;
using Explore.Domain.Enums;

namespace Explore.Domain.Services;

/// <summary>Resolves inherited visitor privacy independently of enrollment and resource authorization.</summary>
public static class VisitorReadPolicy
{
    public static VisitorAccessMode ResolveMode(string? instanceValue, bool isInstanceLocked, string? tenantValue)
    {
        string? raw = isInstanceLocked ? instanceValue : tenantValue ?? instanceValue;
        if (raw is null)
            return VisitorAccessMode.FullRegistrationAndAuth;

        string? value;
        try
        {
            value = JsonSerializer.Deserialize<string>(raw);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The persisted visitor access mode is invalid.", exception);
        }

        return Enum.TryParse(value, out VisitorAccessMode mode)
            && Enum.IsDefined(mode)
            && string.Equals(mode.ToString(), value, StringComparison.Ordinal)
                ? mode
                : throw new InvalidOperationException("The persisted visitor access mode is invalid.");
    }

    public static bool AllowsRead(VisitorAccessMode mode, bool authenticated) => mode switch
    {
        VisitorAccessMode.FullRegistrationAndAuth
            or VisitorAccessMode.AnonymousOnly
            or VisitorAccessMode.DirectoryListingOnly => true,
        VisitorAccessMode.AuthenticatedOnly => authenticated,
        _ => throw new InvalidOperationException("The visitor access mode is invalid.")
    };
}
