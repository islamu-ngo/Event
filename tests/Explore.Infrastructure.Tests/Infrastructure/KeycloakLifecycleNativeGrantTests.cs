using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using Explore.Domain.Keycloak;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel("KeycloakNativeAuthority")]
public sealed class KeycloakLifecycleNativeGrantTests
{
    [Test]
    public async Task MinimalGrantAdmitsRealLifecycleEndpointAndRemovalRevokesIt()
    {
        string realm = $"lifecycle-{Guid.CreateVersion7():N}";
        string clientId = $"sender-{Guid.CreateVersion7():N}";
        string clientSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string administrator = $"bootstrap-{Guid.CreateVersion7():N}";
        string administratorSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var container = new ContainerBuilder(
                $"quay.io/keycloak/keycloak:{KeycloakLifecycleGrantProfile.ProviderVersion}")
            .WithPortBinding(8080, true)
            .WithCommand("start-dev", "--http-port=8080")
            .WithEnvironment("KEYCLOAK_ADMIN", administrator)
            .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", administratorSecret)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                request.ForPath("/realms/master/.well-known/openid-configuration")
                    .ForPort(8080).ForStatusCode(HttpStatusCode.OK),
                wait => wait.WithTimeout(TimeSpan.FromMinutes(2))))
            .Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await container.StartAsync(deadline.Token);
        using var http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false
        })
        {
            BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(8080)}/")
        };

        // Bootstrap credentials are disposable fixture authority, never given to the lifecycle service.
        string bootstrapToken = await TokenAsync(http, "master", new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = administrator,
            ["password"] = administratorSecret
        }, deadline.Token);
        await SendAsync(http, HttpMethod.Post, "admin/realms", bootstrapToken,
            new { realm, enabled = true }, HttpStatusCode.Created, deadline.Token);
        await SendAsync(http, HttpMethod.Post, $"admin/realms/{realm}/clients", bootstrapToken, new
        {
            clientId,
            secret = clientSecret,
            enabled = true,
            protocol = "openid-connect",
            clientAuthenticatorType = "client-secret",
            publicClient = false,
            serviceAccountsEnabled = true,
            standardFlowEnabled = false,
            directAccessGrantsEnabled = false,
            implicitFlowEnabled = false,
            fullScopeAllowed = true
        }, HttpStatusCode.Created, deadline.Token);
        JsonObject client = (await GetAsync(http, $"admin/realms/{realm}/clients?clientId={clientId}",
            bootstrapToken, deadline.Token)).AsArray().Single()!.AsObject();
        string clientUuid = client["id"]!.GetValue<string>();
        JsonObject serviceAccount = (await GetAsync(http,
            $"admin/realms/{realm}/clients/{clientUuid}/service-account-user",
            bootstrapToken, deadline.Token)).AsObject();
        string serviceAccountId = serviceAccount["id"]!.GetValue<string>();
        var defaultRoles = (await GetAsync(http,
            $"admin/realms/{realm}/users/{serviceAccountId}/role-mappings/realm",
            bootstrapToken, deadline.Token)).AsArray();
        if (defaultRoles.Count != 0)
            await SendAsync(http, HttpMethod.Delete,
                $"admin/realms/{realm}/users/{serviceAccountId}/role-mappings/realm",
                bootstrapToken, defaultRoles, HttpStatusCode.NoContent, deadline.Token);
        var managementClient = (await GetAsync(http,
            $"admin/realms/{realm}/clients?clientId=realm-management",
            bootstrapToken, deadline.Token)).AsArray().Single()!.AsObject();
        string managementId = managementClient["id"]!.GetValue<string>();
        JsonObject role = (await GetAsync(http,
            $"admin/realms/{realm}/clients/{managementId}/roles/manage-users",
            bootstrapToken, deadline.Token)).AsObject();
        string mappings = $"admin/realms/{realm}/users/{serviceAccountId}/role-mappings/clients/{managementId}";
        await SendAsync(http, HttpMethod.Post, mappings, bootstrapToken,
            new[] { role }, HttpStatusCode.NoContent, deadline.Token);

        string recipient = $"recipient-{Guid.CreateVersion7():N}";
        // No email: successful permission evaluation stops at provider validation, not mail handoff.
        await SendAsync(http, HttpMethod.Post, $"admin/realms/{realm}/users", bootstrapToken,
            new { username = recipient, enabled = true }, HttpStatusCode.Created, deadline.Token);
        var recipientData = (await GetAsync(http,
            $"admin/realms/{realm}/users?username={recipient}&exact=true",
            bootstrapToken, deadline.Token)).AsArray().Single()!.AsObject();
        string recipientId = recipientData["id"]!.GetValue<string>();
        string issuer = new Uri(http.BaseAddress!, $"realms/{realm}").AbsoluteUri;
        var credentials = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };
        string serviceToken = await TokenAsync(http, realm, credentials, deadline.Token);
        await Assert.That(KeycloakLifecycleGrantProfile.AllowsTokenResponse(serviceToken,
            KeycloakLifecycleGrantProfile.ProviderVersion, issuer, clientId, DateTimeOffset.UtcNow)).IsTrue();
        string lifecycleEndpoint = $"admin/realms/{realm}/users/{recipientId}/execute-actions-email";
        await SendAsync(http, HttpMethod.Put, lifecycleEndpoint, serviceToken,
            new[] { "VERIFY_EMAIL" }, HttpStatusCode.BadRequest, deadline.Token);

        // Validate raw provider grants, including mappings that a narrowed token scope could hide.
        var actualMappings = (await GetAsync(http,
            $"admin/realms/{realm}/users/{serviceAccountId}/role-mappings",
            bootstrapToken, deadline.Token)).AsObject();
        var assignedClients = new JsonObject();
        foreach (var mapping in actualMappings["clientMappings"]!.AsObject())
            assignedClients[mapping.Key] = new JsonArray(mapping.Value!["mappings"]!.AsArray()
                .Select(value => (JsonNode?)JsonValue.Create(value!["name"]!.GetValue<string>())).ToArray());
        serviceAccount["serviceAccountClientId"] = clientId;
        serviceAccount["realmRoles"] = new JsonArray((actualMappings["realmMappings"]?.AsArray() ?? new JsonArray())
            .Select(value => (JsonNode?)JsonValue.Create(value!["name"]!.GetValue<string>())).ToArray());
        serviceAccount["clientRoles"] = assignedClients;
        using var export = JsonDocument.Parse(new JsonObject
        {
            ["realm"] = realm,
            ["clients"] = new JsonArray(client.DeepClone()),
            ["users"] = new JsonArray(serviceAccount.DeepClone()),
            ["roles"] = new JsonObject
            {
                ["client"] = new JsonObject
                {
                    ["realm-management"] = new JsonArray(role.DeepClone())
                }
            }
        }.ToJsonString());
        await Assert.That(KeycloakLifecycleGrantProfile.AllowsDeployment(export.RootElement,
            KeycloakLifecycleGrantProfile.ProviderVersion, realm, clientId)).IsTrue();

        await SendAsync(http, HttpMethod.Delete, mappings, bootstrapToken,
            new[] { role }, HttpStatusCode.NoContent, deadline.Token);
        string revokedToken = await TokenAsync(http, realm, credentials, deadline.Token);
        await Assert.That(KeycloakLifecycleGrantProfile.AllowsTokenResponse(revokedToken,
            KeycloakLifecycleGrantProfile.ProviderVersion, issuer, clientId, DateTimeOffset.UtcNow)).IsFalse();
        await SendAsync(http, HttpMethod.Put, lifecycleEndpoint, revokedToken,
            new[] { "VERIFY_EMAIL" }, HttpStatusCode.Forbidden, deadline.Token);
    }

    private static async Task<string> TokenAsync(
        HttpClient http, string realm, Dictionary<string, string> form, CancellationToken token)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync($"realms/{realm}/protocol/openid-connect/token", content, token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return payload.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<JsonNode> GetAsync(
        HttpClient http, string path, string credential, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        using var response = await http.SendAsync(request, token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!;
    }

    private static async Task SendAsync<T>(
        HttpClient http, HttpMethod method, string path, string credential, T body,
        HttpStatusCode expected, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        using var response = await http.SendAsync(request, token);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }
}
