using System.Text.Json;

namespace Explore.Domain.Services;

/// <summary>Resolves enrollment independently of existing-account login and visitor privacy.</summary>
public static class TenantUserRegistrationPolicy
{
    public static bool ResolveEnabled(string? instanceValue, bool isInstanceLocked, string? tenantValue)
    {
        string? raw = isInstanceLocked ? instanceValue : tenantValue ?? instanceValue;
        if (raw is null)
            return true;

        try
        {
            return JsonSerializer.Deserialize<bool>(raw);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The persisted tenant enrollment policy is invalid.", exception);
        }
    }
}
