using System.Text.Json;

namespace Explore.Domain.Keycloak;

/// <summary>
/// The deployment and fresh token-response grant contract for the pinned lifecycle client.
/// Token claims are inspected only after transport-authenticated client-credentials exchange;
/// this is not a validator for caller-supplied bearer tokens.
/// </summary>
public static class KeycloakLifecycleGrantProfile
{
    public const string ProviderVersion = "26.1.2";
    public const string ManagementClient = "realm-management";
    public const string RequiredRole = "manage-users";

    public static bool AllowsDeployment(
        JsonElement export, string providerVersion, string realm, string clientId)
    {
        if (string.IsNullOrWhiteSpace(realm) || string.IsNullOrWhiteSpace(clientId)
            || export.ValueKind != JsonValueKind.Object
            || providerVersion != ProviderVersion || String(export, "realm") != realm
            || !export.TryGetProperty("clients", out var clients) || clients.ValueKind != JsonValueKind.Array
            || !export.TryGetProperty("users", out var users) || users.ValueKind != JsonValueKind.Array)
            return false;
        var matchingClients = clients.EnumerateArray().Where(client => String(client, "clientId") == clientId).ToArray();
        var matchingUsers = users.EnumerateArray().Where(user =>
            String(user, "serviceAccountClientId") == clientId).ToArray();
        if (matchingClients.Length != 1 || matchingUsers.Length != 1)
            return false;
        var client = matchingClients[0];
        var user = matchingUsers[0];
        if (!Flag(client, "enabled", true) || !Flag(client, "serviceAccountsEnabled", true)
            || !Flag(client, "publicClient", false) || !Flag(client, "standardFlowEnabled", false)
            || !Flag(client, "directAccessGrantsEnabled", false) || !Flag(client, "implicitFlowEnabled", false)
            || String(client, "protocol") != "openid-connect"
            || String(client, "clientAuthenticatorType") != "client-secret"
            || String(user, "username") != $"service-account-{clientId}"
            || !EmptyRoles(user, "realmRoles")
            || !user.TryGetProperty("clientRoles", out var assigned) || !ExactRoles(assigned, nested: false))
            return false;
        if (!export.TryGetProperty("roles", out var definitions)
            || definitions.ValueKind != JsonValueKind.Object
            || !definitions.TryGetProperty("client", out var clientDefinitions)
            || clientDefinitions.ValueKind != JsonValueKind.Object
            || !clientDefinitions.TryGetProperty(ManagementClient, out var managementRoles)
            || managementRoles.ValueKind != JsonValueKind.Array)
            return false;
        var role = managementRoles.EnumerateArray().Where(value => String(value, "name") == RequiredRole).ToArray();
        return role.Length == 1 && Flag(role[0], "composite", false);
    }

    public static bool AllowsTokenResponse(
        string token, string providerVersion, string issuer, string clientId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(clientId)
            || providerVersion != ProviderVersion || token.Length > 65_536)
            return false;
        string[] segments = token.Split('.');
        if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty))
            return false;
        try
        {
            using var header = JsonDocument.Parse(Decode(segments[0]));
            using var payload = JsonDocument.Parse(Decode(segments[1]));
            var claims = payload.RootElement;
            if (String(header.RootElement, "alg") != "RS256"
                || String(claims, "iss") != issuer || String(claims, "azp") != clientId
                || String(claims, "preferred_username") != $"service-account-{clientId}"
                || string.IsNullOrWhiteSpace(String(claims, "sub"))
                || !claims.TryGetProperty("exp", out var expiry) || expiry.ValueKind != JsonValueKind.Number
                || !expiry.TryGetInt64(out long expiresAt) || expiresAt <= now.ToUnixTimeSeconds()
                || !claims.TryGetProperty("resource_access", out var grants) || !ExactRoles(grants, nested: true))
                return false;
            return !claims.TryGetProperty("realm_access", out var realmAccess)
                || realmAccess.ValueKind == JsonValueKind.Object && EmptyRoles(realmAccess, "roles");
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static byte[] Decode(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
    }

    private static bool ExactRoles(JsonElement grants, bool nested)
    {
        if (grants.ValueKind != JsonValueKind.Object)
            return false;
        var clients = grants.EnumerateObject().ToArray();
        if (clients.Length != 1 || clients[0].Name != ManagementClient)
            return false;
        var roles = clients[0].Value;
        if (nested && (roles.ValueKind != JsonValueKind.Object || !roles.TryGetProperty("roles", out roles)))
            return false;
        return roles.ValueKind == JsonValueKind.Array && roles.GetArrayLength() == 1
            && roles[0].ValueKind == JsonValueKind.String && roles[0].GetString() == RequiredRole;
    }

    private static bool EmptyRoles(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && (!element.TryGetProperty(property, out var roles)
            || roles.ValueKind == JsonValueKind.Array && roles.GetArrayLength() == 0);

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string property, bool expected) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);
}
