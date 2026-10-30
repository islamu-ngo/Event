using System.Security.Claims;
using Event.Web.BffHosting.Security;

namespace Explore.Blazor.Services.Auth;

public static class BffSessionIdentity
{
    public const string HandleClaim = "urn:islamu:event:bff-session";
    public const string HandleProperty = ".event.bff-session";
    public const string RevisionProperty = ".event.bff-session-revision";

    public static bool TryGetBinding(ClaimsPrincipal principal, out BffSessionBinding binding)
    {
        binding = default;
        var handles = principal.FindAll(HandleClaim).ToArray();
        if (handles.Length != 1 || !IsValidHandle(handles[0].Value)
            || !principal.TryGetCircuitSubject(out var owner)) return false;
        binding = new(handles[0].Value, owner.PartitionKey);
        return true;
    }

    internal static bool IsValidHandle(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
