namespace Explore.ServiceDefaults.Secrets;

public static class SecretAcquisitionPolicy
{
    private static readonly string[] ApiRoots =
    [
        "/runtime/api/authentication/keycloak", "/runtime/api/keycloak/lifecycle",
        "/runtime/api/authorization/cerbos", "/database", "/database/erasure",
        "/database/identity", "/postgresql", "/api", "/api/operator-identity",
        "/api/operatoridentity", "/api/bootstrap", "/api/instance-bootstrap",
        "/api/instancebootstrap", "/api/controlplane", "/api/control-plane",
        "/api/webpush", "/api/web-push", "/api/ratelimiting", "/api/rate-limiting",
        "/mcp", "/ai", "/storage", "/smtp", "/stripe", "/integrations/listmonk",
        "/setup", "/promotions", "/admissions", "/ticketing/recovery", "/atproto",
        "/webhook", "/messaging", "/reporting", "/registration-providers",
        "/analytics", "/localization"
    ];
    private static readonly string[] BffRoots =
        ["/runtime/bff/oidc/keycloak", "/runtime/bff/oidc/google", "/runtime/bff/atproto/oauth"];
    private static readonly string[] MigrationRoots =
        ["/database", "/database/erasure", "/database/identity", "/postgresql"];

    public static bool AllowsRoot(SecretConsumerRole consumer, string folder) => consumer switch
    {
        SecretConsumerRole.Api => ApiRoots.Contains(folder, StringComparer.Ordinal),
        SecretConsumerRole.Bff => BffRoots.Contains(folder, StringComparer.Ordinal),
        SecretConsumerRole.Combined => ApiRoots.Contains(folder, StringComparer.Ordinal)
            || BffRoots.Contains(folder, StringComparer.Ordinal),
        SecretConsumerRole.Migration => MigrationRoots.Contains(folder, StringComparer.Ordinal),
        _ => false
    };

    public static void Admit(SecretAuthoritySelection selection, IEnumerable<SecretAcquisitionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(entries);
        selection.Validate();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool any = false;
        foreach (SecretAcquisitionEntry entry in entries)
        {
            any = true;
            if (entry is null || !AllowsRoot(selection.Consumer, entry.Folder)
                || string.IsNullOrWhiteSpace(entry.Key) || entry.Key.Contains('/', StringComparison.Ordinal)
                || entry.Key.Contains("${", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(entry.ConfigurationKey)
                || IsAuthorityInput(entry.Key) || IsAuthorityInput(entry.ConfigurationKey)
                || !destinations.Add(entry.ConfigurationKey))
                throw new InvalidOperationException("secret_consumer_scope_invalid");
        }
        if (!any)
            throw new InvalidOperationException("secret_consumer_scope_invalid");
    }

    private static bool IsAuthorityInput(string name) =>
        string.Equals(name, "SECRET_PROVIDER", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "ASPNETCORE_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("SecretProvider", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Infisical", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("INFISICAL_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("APPHOST_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("APPHOST:", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("DATA_PROTECTION_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("DATA_PROTECTION:", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("EVENT_DP_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("EVENT_DP:", StringComparison.OrdinalIgnoreCase);
}
