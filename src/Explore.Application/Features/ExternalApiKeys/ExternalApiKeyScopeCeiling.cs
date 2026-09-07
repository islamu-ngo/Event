using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Features.ExternalApiKeys;

/// <summary>
/// Maps each <see cref="ExternalApiKeyOwnerType"/> to its maximum allowed scopes.
/// A key cannot be created or updated with scopes exceeding its owner type ceiling.
/// </summary>
public static class ExternalApiKeyScopeCeiling
{
    private static readonly IReadOnlySet<string> UserScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ExternalApiKeyScopes.EventsRead,
        ExternalApiKeyScopes.EventsWrite,
        ExternalApiKeyScopes.UsersRead,
        ExternalApiKeyScopes.UsersWrite,
        ExternalApiKeyScopes.LookupsRead,
        ExternalApiKeyScopes.McpRead,
        ExternalApiKeyScopes.McpPropose,
        ExternalApiKeyScopes.RegistrationsWrite,
        ExternalApiKeyScopes.ApiKeysManage
    };

    private static readonly IReadOnlySet<string> OrganizationScopes = new HashSet<string>(UserScopes, StringComparer.OrdinalIgnoreCase)
    {
        ExternalApiKeyScopes.OrganizationsRead,
        ExternalApiKeyScopes.OrganizationsWrite
    };

    private static readonly IReadOnlySet<string> GroupScopes = new HashSet<string>(UserScopes, StringComparer.OrdinalIgnoreCase)
    {
        ExternalApiKeyScopes.GroupsRead,
        ExternalApiKeyScopes.GroupsWrite
    };

    private static readonly IReadOnlySet<string> TenantScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Tenant gets all user + org + group scopes + admin:tenant
        ExternalApiKeyScopes.EventsRead,
        ExternalApiKeyScopes.EventsWrite,
        ExternalApiKeyScopes.UsersRead,
        ExternalApiKeyScopes.UsersWrite,
        ExternalApiKeyScopes.LookupsRead,
        ExternalApiKeyScopes.McpRead,
        ExternalApiKeyScopes.McpPropose,
        ExternalApiKeyScopes.RegistrationsWrite,
        ExternalApiKeyScopes.ApiKeysManage,
        ExternalApiKeyScopes.OrganizationsRead,
        ExternalApiKeyScopes.OrganizationsWrite,
        ExternalApiKeyScopes.GroupsRead,
        ExternalApiKeyScopes.GroupsWrite,
        ExternalApiKeyScopes.AdminTenant
    };

    // InstanceAdmin gets everything
    private static readonly IReadOnlySet<string> InstanceAdminScopes = ExternalApiKeyScopes.All;

    /// <summary>
    /// Returns the ceiling scope set for the given owner type.
    /// </summary>
    public static IReadOnlySet<string> GetCeiling(ExternalApiKeyOwnerType ownerType)
    {
        return ownerType switch
        {
            ExternalApiKeyOwnerType.User => UserScopes,
            ExternalApiKeyOwnerType.Organization => OrganizationScopes,
            ExternalApiKeyOwnerType.Group => GroupScopes,
            ExternalApiKeyOwnerType.Tenant => TenantScopes,
            ExternalApiKeyOwnerType.InstanceAdmin => InstanceAdminScopes,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Returns true if all <paramref name="scopes"/> are within the ceiling for <paramref name="ownerType"/>.
    /// </summary>
    public static bool AreWithinCeiling(ExternalApiKeyOwnerType ownerType, IEnumerable<string> scopes)
    {
        var ceiling = GetCeiling(ownerType);
        return scopes.All(scope => ceiling.Contains(scope));
    }

    /// <summary>
    /// Returns the scopes that exceed the ceiling for the given owner type.
    /// </summary>
    public static IReadOnlyList<string> GetExceeding(ExternalApiKeyOwnerType ownerType, IEnumerable<string> scopes)
    {
        var ceiling = GetCeiling(ownerType);
        return scopes.Where(scope => !ceiling.Contains(scope)).ToList();
    }
}
