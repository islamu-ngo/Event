using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;
using Explore.Infrastructure.Services.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Runtime)]
[Category(TestCategories.Security)]
[ClassDataSource<KeycloakOnlyFixture>(
    Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public sealed class KeycloakOperationRealRuntimeTests(
    KeycloakOnlyFixture fixture)
{
    [Test]
    public async Task ExistingRealmConflictPreservesProviderState()
    {
        using var httpClient = new HttpClient();
        string adminToken = await RequestAdminTokenAsync(
            httpClient);
        JsonObject before = await ReadPreservationSnapshotAsync(
            httpClient,
            adminToken);
        _ = await fixture.TokenClient.GetUserTokenAsync();
        _ = await fixture.TokenClient.GetTenantAdminTokenAsync();

        using var operationHttpClient = new HttpClient();
        KeycloakAdminClient client =
            CreateClient(operationHttpClient);
        KeycloakDesiredProjection desired =
            KeycloakDesiredProjection.Realm(
                KeycloakContainerFixture.RealmName,
                Guid.CreateVersion7().ToString("D"));
        var step = new KeycloakChangeStep(
            "realm:create",
            KeycloakStep.CreateRealm,
            KeycloakResourceKind.Realm,
            KeycloakContainerFixture.RealmName,
            KeycloakStepPrecondition.MustBeAbsent,
            expectedFingerprint: null,
            expectedIdentityFingerprint: null,
            KeycloakOperationService
                .DesiredProvisioningFingerprint(desired),
            bindingFingerprint: "real-runtime-binding",
            desired);
        var request = new KeycloakProvisioningOperationRequest(
            new Uri(fixture.Authority),
            KeycloakContainerFixture.RealmName,
            KeycloakContainerFixture.TestClientId,
            "islamu-event-api",
            step,
            runtimeClientSecret: null,
            administratorUsername: "admin",
            administratorPassword:
                fixture.BootstrapAdminPassword);

        KeycloakProvisioningOperationResult result =
            await client.ApplyApprovedProvisioningAsync(
                request,
                CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(KeycloakStepOutcomeKind.Conflict);
        JsonObject after = await ReadPreservationSnapshotAsync(
            httpClient,
            adminToken);
        await Assert.That(JsonNode.DeepEquals(before, after))
            .IsTrue();
        _ = await fixture.TokenClient.GetUserTokenAsync();
        _ = await fixture.TokenClient.GetTenantAdminTokenAsync();
    }

    [Test]
    public async Task PlannedIds_AreAcceptedForNewRealmClientAndMapper()
    {
        string suffix = Guid.CreateVersion7().ToString("N");
        string realm = $"operator-{suffix}";
        string clientId = $"bff-{suffix}";
        string realmId = Guid.CreateVersion7().ToString("D");
        string clientProviderId =
            Guid.CreateVersion7().ToString("D");
        string mapperProviderId =
            Guid.CreateVersion7().ToString("D");
        using var httpClient = new HttpClient();
        string adminToken = await RequestAdminTokenAsync(
            httpClient);
        try
        {
            using var operationHttpClient = new HttpClient();
            KeycloakAdminClient client =
                CreateClient(operationHttpClient);
            Uri authority = new(
                $"{fixture.KeycloakBaseUrl}/realms/{realm}");

            KeycloakDesiredProjection realmDesired =
                KeycloakDesiredProjection.Realm(
                    realm,
                    realmId);
            KeycloakProvisioningOperationResult realmResult =
                await client.ApplyApprovedProvisioningAsync(
                    ProvisioningRequest(
                        authority,
                        realm,
                        clientId,
                        RealmStep(realmDesired),
                        runtimeClientSecret: null),
                    CancellationToken.None);

            await Assert.That(realmResult.Outcome)
                .IsEqualTo(
                    KeycloakStepOutcomeKind.Applied);
            await Assert.That(realmResult.ProviderResourceId)
                .IsEqualTo(realmId);

            string callback =
                $"https://event.example.test/{suffix}/signin";
            KeycloakDesiredProjection clientDesired =
                KeycloakDesiredProjection.ConfidentialClient(
                    clientId,
                    [callback],
                    ["https://event.example.test"],
                    clientProviderId);
            KeycloakProvisioningOperationResult clientResult =
                await client.ApplyApprovedProvisioningAsync(
                    ProvisioningRequest(
                        authority,
                        realm,
                        clientId,
                        ClientStep(clientDesired),
                        $"runtime-{Guid.CreateVersion7():N}"),
                    CancellationToken.None);

            await Assert.That(clientResult.Outcome)
                .IsEqualTo(
                    KeycloakStepOutcomeKind.Applied);
            await Assert.That(clientResult.ProviderResourceId)
                .IsEqualTo(clientProviderId);

            const string mapperName = "islamu-subject";
            KeycloakDesiredProjection mapperDesired =
                KeycloakDesiredProjection.Mapper(
                    mapperName,
                    KeycloakMapperSemantic.Subject,
                    providerResourceId: mapperProviderId);
            var mapperStep = new KeycloakChangeStep(
                "mapper:subject",
                KeycloakStep.CreateMapper,
                KeycloakResourceKind.ProtocolMapper,
                clientId,
                KeycloakStepPrecondition.MustBeAbsent,
                expectedFingerprint: null,
                expectedIdentityFingerprint: null,
                KeycloakOperationService
                    .DesiredMapperFingerprint(
                        new KeycloakInspectionSnapshot(
                            realm,
                            clientId,
                            apiClientId: null,
                            realmExists: true),
                        KeycloakMapperSemantic.Subject),
                bindingFingerprint: "real-runtime-binding",
                mapperDesired);
            KeycloakMapperOperationResult mapperResult =
                await client.ApplyApprovedMapperAsync(
                    new KeycloakMapperOperationRequest(
                        authority,
                        realm,
                        clientId,
                        apiClientId: null,
                        mapperName,
                        KeycloakMapperSemantic.Subject,
                        mapperStep,
                        "admin",
                        fixture.BootstrapAdminPassword),
                    CancellationToken.None);

            await Assert.That(mapperResult.Outcome)
                .IsEqualTo(
                    KeycloakStepOutcomeKind.Applied);
            await Assert.That(mapperResult.ProviderResourceId)
                .IsEqualTo(mapperProviderId);
        }
        finally
        {
            await DeleteRealmAsync(
                httpClient,
                adminToken,
                realm);
        }
    }

    private KeycloakProvisioningOperationRequest
        ProvisioningRequest(
            Uri authority,
            string realm,
            string clientId,
            KeycloakChangeStep step,
            string? runtimeClientSecret) =>
        new(
            authority,
            realm,
            clientId,
            apiClientId: null,
            step,
            runtimeClientSecret,
            "admin",
            fixture.BootstrapAdminPassword);

    private static KeycloakChangeStep RealmStep(
        KeycloakDesiredProjection desired) =>
        new(
            "realm:create",
            KeycloakStep.CreateRealm,
            KeycloakResourceKind.Realm,
            desired.ResourceName,
            KeycloakStepPrecondition.MustBeAbsent,
            expectedFingerprint: null,
            expectedIdentityFingerprint: null,
            KeycloakOperationService
                .DesiredProvisioningFingerprint(desired),
            bindingFingerprint: "real-runtime-binding",
            desired);

    private static KeycloakChangeStep ClientStep(
        KeycloakDesiredProjection desired) =>
        new(
            "client:bff",
            KeycloakStep.CreateClient,
            KeycloakResourceKind.Client,
            desired.ResourceName,
            KeycloakStepPrecondition.MustBeAbsent,
            expectedFingerprint: null,
            expectedIdentityFingerprint: null,
            KeycloakOperationService
                .DesiredProvisioningFingerprint(desired),
            bindingFingerprint: "real-runtime-binding",
            desired);

    private KeycloakAdminClient CreateClient(
        HttpClient httpClient)
    {
        IHostEnvironment environment =
            Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        Microsoft.Extensions.Configuration.IConfiguration
            configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Keycloak:AllowDevelopmentLoopbackHttp"] =
                            "true"
                    })
                .Build();
        return new KeycloakAdminClient(
            httpClient,
            environment,
            configuration);
    }

    private async Task<string> RequestAdminTokenAsync(
        HttpClient httpClient)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{fixture.KeycloakBaseUrl}/realms/master/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "password"),
                new("client_id", "admin-cli"),
                new("username", "admin"),
                new(
                    "password",
                    fixture.BootstrapAdminPassword)
            ])
        };
        using HttpResponseMessage response =
            await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using JsonDocument payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return payload.RootElement
            .GetProperty("access_token")
            .GetString()
            ?? throw new InvalidOperationException(
                "Keycloak admin token was empty.");
    }

    private async Task DeleteRealmAsync(
        HttpClient httpClient,
        string adminToken,
        string realm)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{fixture.KeycloakBaseUrl}/admin/realms/"
            + Uri.EscapeDataString(realm));
        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                adminToken);
        using HttpResponseMessage response =
            await httpClient.SendAsync(request);
        if (response.StatusCode
            != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<JsonObject>
        ReadPreservationSnapshotAsync(
            HttpClient httpClient,
            string adminToken)
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                adminToken);
        string root =
            $"{fixture.KeycloakBaseUrl}/admin/realms/{KeycloakContainerFixture.RealmName}";
        JsonObject snapshot = new()
        {
            ["realm"] = await ReadNodeAsync(
                httpClient,
                root),
            ["clients"] = await ReadNodeAsync(
                httpClient,
                $"{root}/clients?max=100"),
            ["roles"] = await ReadNodeAsync(
                httpClient,
                $"{root}/roles?max=100"),
            ["groups"] = await ReadNodeAsync(
                httpClient,
                $"{root}/groups?max=100")
        };
        JsonArray users =
            (await ReadNodeAsync(
                httpClient,
                $"{root}/users?max=100")).AsArray();
        snapshot["users"] = users.DeepClone();
        var roleMappings = new JsonObject();
        var memberships = new JsonObject();
        foreach (JsonNode? user in users)
        {
            string id = user!["id"]!.GetValue<string>();
            roleMappings[id] = await ReadNodeAsync(
                httpClient,
                $"{root}/users/{Uri.EscapeDataString(id)}/role-mappings");
            memberships[id] = await ReadNodeAsync(
                httpClient,
                $"{root}/users/{Uri.EscapeDataString(id)}/groups");
        }

        snapshot["roleMappings"] = roleMappings;
        snapshot["memberships"] = memberships;
        return snapshot;
    }

    private static async Task<JsonNode> ReadNodeAsync(
        HttpClient httpClient,
        string uri) =>
        await httpClient.GetFromJsonAsync<JsonNode>(uri)
        ?? throw new InvalidOperationException(
            "Keycloak preservation response was empty.");
}
