namespace Explore.Domain.Constants;

/// <summary>
/// Well-known external API key scopes following the {resource}:{action} format.
/// Every scope granted to an API key must be a member of this catalog.
/// </summary>
public static class ExternalApiKeyScopes
{
    // ===== Read Scopes =====
    public const string EventsRead = "events:read";
    public const string OrganizationsRead = "organizations:read";
    public const string GroupsRead = "groups:read";
    public const string UsersRead = "users:read";
    public const string LookupsRead = "lookups:read";
    public const string McpRead = "mcp:read";

    // ===== Write Scopes =====
    public const string EventsWrite = "events:write";
    public const string OrganizationsWrite = "organizations:write";
    public const string GroupsWrite = "groups:write";
    public const string UsersWrite = "users:write";
    public const string RegistrationsWrite = "registrations:write";
    public const string McpPropose = "mcp:propose";

    // ===== Management Scopes =====
    public const string ApiKeysManage = "api-keys:manage";

    // ===== Admin Scopes =====
    public const string AdminTenant = "admin:tenant";
    public const string AdminInstance = "admin:instance";

    /// <summary>
    /// Complete set of all valid v1 scopes. Used for catalog membership validation.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EventsRead,
        OrganizationsRead,
        GroupsRead,
        UsersRead,
        LookupsRead,
        McpRead,
        EventsWrite,
        OrganizationsWrite,
        GroupsWrite,
        UsersWrite,
        RegistrationsWrite,
        McpPropose,
        ApiKeysManage,
        AdminTenant,
        AdminInstance
    };

    /// <summary>
    /// Returns true if every scope in <paramref name="scopes"/> is a recognized catalog member.
    /// </summary>
    public static bool AreAllValid(IEnumerable<string> scopes)
    {
        return scopes.All(scope => All.Contains(scope));
    }

    /// <summary>
    /// Returns the scopes from <paramref name="scopes"/> that are not in the catalog.
    /// </summary>
    public static IReadOnlyList<string> GetInvalid(IEnumerable<string> scopes)
    {
        return scopes.Where(scope => !All.Contains(scope)).ToList();
    }
}
