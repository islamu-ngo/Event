namespace Explore.Blazor.Client.Constants;

/// <summary>
/// Available scopes for external API keys, matching the server-side v1 scope catalog.
/// Format follows <c>{resource}:{action}</c> convention.
/// </summary>
public static class ExternalApiKeyConstants
{
    public static readonly IReadOnlyList<ScopeDefinition> AvailableScopes =
    [
        new("events:read", "Events", "Read event data"),
        new("events:write", "Events", "Create and update events"),
        new("organizations:read", "Organizations", "Read organization data"),
        new("organizations:write", "Organizations", "Create and update organizations"),
        new("groups:read", "Groups", "Read group data"),
        new("groups:write", "Groups", "Create and update groups"),
        new("users:read", "Users", "Read user profiles"),
        new("users:write", "Users", "Update user data"),
        new("lookups:read", "Lookups", "Read lookup/reference data"),
        new("registrations:write", "Registrations", "Manage event registrations"),
        new("api-keys:manage", "API Keys", "Manage API keys"),
        new("admin:tenant", "Administration", "Tenant administration"),
        new("admin:instance", "Administration", "Instance-wide administration"),
    ];

    /// <summary>
    /// Scope ceilings per owner type — mirrors server-side ExternalApiKeyScopeCeiling.
    /// Only scopes within the ceiling are offered in the dialog.
    /// </summary>
    private static readonly Dictionary<int, HashSet<string>> ScopeCeilings = new()
    {
        [1] = ["events:read", "events:write", "users:read", "users:write", "lookups:read", "registrations:write", "api-keys:manage"], // User
        [2] = ["events:read", "events:write", "users:read", "users:write", "lookups:read", "registrations:write", "api-keys:manage", "organizations:read", "organizations:write"], // Organization
        [3] = ["events:read", "events:write", "users:read", "users:write", "lookups:read", "registrations:write", "api-keys:manage", "groups:read", "groups:write"], // Group
        [4] = ["events:read", "events:write", "users:read", "users:write", "lookups:read", "registrations:write", "api-keys:manage", "organizations:read", "organizations:write", "groups:read", "groups:write", "admin:tenant"], // Tenant
        [5] = ["events:read", "events:write", "users:read", "users:write", "lookups:read", "registrations:write", "api-keys:manage", "organizations:read", "organizations:write", "groups:read", "groups:write", "admin:tenant", "admin:instance"], // InstanceAdmin
    };

    /// <summary>Returns scope definitions allowed for the given owner type.</summary>
    public static IReadOnlyList<ScopeDefinition> GetAvailableScopesForOwnerType(int ownerType)
    {
        if (!ScopeCeilings.TryGetValue(ownerType, out var ceiling))
            return AvailableScopes;

        return AvailableScopes.Where(s => ceiling.Contains(s.Value)).ToList();
    }

    /// <summary>Maps OwnerType enum integers to display labels.</summary>
    public static string GetOwnerTypeLabel(int? ownerType) => ownerType switch
    {
        1 => "User",
        2 => "Organization",
        3 => "Group",
        4 => "Tenant",
        5 => "Instance Admin",
        _ => "Unknown",
    };

    /// <summary>Maps status enum integers to display labels.</summary>
    public static string GetStatusLabel(int? status) => status switch
    {
        1 => "Active",
        2 => "Revoked",
        3 => "Expired",
        4 => "Suspended",
        5 => "Pending Rotation",
        _ => "Unknown",
    };

    /// <summary>Returns the MudBlazor Color for a status chip.</summary>
    public static MudBlazor.Color GetStatusColor(int? status) => status switch
    {
        1 => MudBlazor.Color.Success,
        2 => MudBlazor.Color.Error,
        3 => MudBlazor.Color.Warning,
        4 => MudBlazor.Color.Dark,
        5 => MudBlazor.Color.Info,
        _ => MudBlazor.Color.Default,
    };

    public record ScopeDefinition(string Value, string Category, string Description);
}
