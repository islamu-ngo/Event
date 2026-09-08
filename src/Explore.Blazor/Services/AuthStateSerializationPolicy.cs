using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Services;

public static class AuthStateSerializationPolicy
{
    private static readonly HashSet<string> DisplayClaimTypes = new(StringComparer.Ordinal)
    {
        ClaimTypes.Name,
        "name",
        "preferred_username",
        "given_name",
        "family_name"
    };

    public static ValueTask<AuthenticationStateData?> SerializeDisplaySafeClaimsAsync(AuthenticationState authenticationState)
    {
        var principal = authenticationState.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return ValueTask.FromResult<AuthenticationStateData?>(null);
        }

        var data = new AuthenticationStateData();
        if (principal.Identities.FirstOrDefault() is { } identity)
        {
            data.NameClaimType = identity.NameClaimType;
            data.RoleClaimType = identity.RoleClaimType;
        }

        foreach (var claim in GetDisplaySafeClaims(principal, data.NameClaimType))
        {
            data.Claims.Add(new ClaimData(claim));
        }

        return ValueTask.FromResult<AuthenticationStateData?>(data);
    }

    private static IEnumerable<Claim> GetDisplaySafeClaims(ClaimsPrincipal principal, string nameClaimType)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in principal.Claims)
        {
            if (!IsDisplaySafeClaim(claim, nameClaimType))
            {
                continue;
            }

            var key = string.Concat(claim.Type, "\u001f", claim.Value);
            if (seen.Add(key))
            {
                yield return claim;
            }
        }
    }

    private static bool IsDisplaySafeClaim(Claim claim, string nameClaimType)
    {
        if (string.IsNullOrWhiteSpace(claim.Value))
        {
            return false;
        }

        return string.Equals(claim.Type, nameClaimType, StringComparison.Ordinal)
            || DisplayClaimTypes.Contains(claim.Type);
    }
}
