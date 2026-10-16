using System.Text;
using System.Text.Json;
using Explore.Domain.Keycloak;

namespace Event.Domain.UnitTests.Keycloak;

public sealed class KeycloakLifecycleGrantProfileTests
{
    [Test]
    [Arguments("valid", true)]
    [Arguments("foreign_issuer", false)]
    [Arguments("foreign_client", false)]
    [Arguments("human", false)]
    [Arguments("extra_management_role", false)]
    [Arguments("extra_client_grant", false)]
    [Arguments("realm_role", false)]
    [Arguments("expired", false)]
    [Arguments("unsupported_version", false)]
    public async Task OnlyFreshSameRealmDedicatedMinimalGrantMaySendLifecycleActions(string mutation, bool expected)
    {
        var now = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        const string issuer = "https://identity.example.test/realms/operators";
        const string client = "event-lifecycle";
        var grants = new Dictionary<string, object>
        {
            ["realm-management"] = new
            {
                roles = mutation == "extra_management_role"
                    ? new[] { "manage-users", "realm-admin" } : ["manage-users"]
            }
        };
        if (mutation == "extra_client_grant")
            grants["account"] = new { roles = new[] { "manage-account" } };
        string header = Segment(new { alg = "RS256" });
        string payload = Segment(new
        {
            iss = mutation == "foreign_issuer" ? "https://foreign.example.test/realms/operators" : issuer,
            azp = mutation == "foreign_client" ? "event-bff" : client,
            sub = Guid.CreateVersion7().ToString(),
            preferred_username = mutation == "human" ? "operator" : $"service-account-{client}",
            exp = now.AddMinutes(mutation == "expired" ? -1 : 5).ToUnixTimeSeconds(),
            resource_access = grants,
            realm_access = new { roles = mutation == "realm_role" ? new[] { "offline_access" } : [] }
        });
        string token = $"{header}.{payload}.{Segment(Guid.CreateVersion7().ToString())}";
        bool accepted = KeycloakLifecycleGrantProfile.AllowsTokenResponse(token,
            mutation == "unsupported_version" ? "99.0.0" : KeycloakLifecycleGrantProfile.ProviderVersion,
            issuer, client, now);
        await Assert.That(accepted).IsEqualTo(expected);
    }

    [Test]
    [Arguments(false, false, true)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    public async Task DeploymentExportRejectsPasswordFlowAndExtraMappedPrivileges(
        bool passwordGrant, bool excessRole, bool expected)
    {
        using var export = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            realm = "operators",
            clients = new[]
            {
                new
                {
                    clientId = "event-lifecycle", enabled = true, serviceAccountsEnabled = true,
                    publicClient = false, standardFlowEnabled = false, directAccessGrantsEnabled = passwordGrant,
                    implicitFlowEnabled = false, protocol = "openid-connect", clientAuthenticatorType = "client-secret"
                }
            },
            users = new[]
            {
                new
                {
                    serviceAccountClientId = "event-lifecycle", username = "service-account-event-lifecycle",
                    realmRoles = Array.Empty<string>(),
                    clientRoles = new Dictionary<string, string[]>
                    {
                        ["realm-management"] = excessRole ? ["manage-users", "realm-admin"] : ["manage-users"]
                    }
                }
            },
            roles = new
            {
                client = new Dictionary<string, object[]>
                {
                    ["realm-management"] = [new { name = "manage-users", composite = false }]
                }
            }
        }));
        await Assert.That(KeycloakLifecycleGrantProfile.AllowsDeployment(export.RootElement,
            KeycloakLifecycleGrantProfile.ProviderVersion, "operators", "event-lifecycle")).IsEqualTo(expected);
    }

    private static string Segment<T>(T value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
