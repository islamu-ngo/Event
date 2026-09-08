namespace Explore.Blazor.Client.Models.Responses;

/// <summary>
/// Result of a BFF mutation (POST/PUT/DELETE) called via JS interop fetch.
/// Property names are camelCase in JS; System.Text.Json Web defaults handle the mapping.
/// </summary>
public class BffMutationResult
{
    public bool Ok { get; set; }
    public int Status { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Status of the persisted setup secret, retrieved via GET /bff/setup-secret.
/// </summary>
public class SetupSecretStatusResponse
{
    public bool HasPersistedSecret { get; set; }
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Response from GET /auth/providers containing available auth provider quick actions.
/// </summary>
public class AuthProvidersResponse
{
    public List<AuthProviderQuickAction> Providers { get; set; } = [];
}

/// <summary>
/// A single auth provider quick action (e.g., Keycloak button, Google button).
/// </summary>
public class AuthProviderQuickAction
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
