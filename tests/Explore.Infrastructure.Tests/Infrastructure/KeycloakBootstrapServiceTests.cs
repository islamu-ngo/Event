using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Onboarding;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Keycloak;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class KeycloakBootstrapServiceTests
{
    [Test]
    public async Task BootstrapAsync_CreateRealmMode_CreatesRealmClientsAndUpdatesSecrets()
    {
        var request = CreateRequest(mode: KeycloakBootstrapMode.CreateRealm);
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ => new HttpResponseMessage(HttpStatusCode.NotFound)),
            Expect(HttpMethod.Post, "/auth/admin/realms", _ => new HttpResponseMessage(HttpStatusCode.Created)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("[]")),
            Expect(HttpMethod.Post, "/auth/admin/realms/ISLAMU/clients", _ => new HttpResponseMessage(HttpStatusCode.Created)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => JsonResponse(ClientRepresentationJson("blazor-uuid", "islamu-event-blazor"))),
            ExpectOfflineAccessRole(),
            ExpectDefaultRole(),
            ExpectDefaultRoleCompositeUpdate(),
            ExpectOfflineAccessScopeLookup(),
            ExpectOfflineAccessRole(),
            ExpectOfflineAccessScopeMappingUpdate(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/client-secret", _ => JsonResponse("""
                { "value": "old-blazor-secret" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/client-scopes", _ => JsonResponse("""
                [{ "id": "offline-scope-uuid", "name": "offline_access" }]
                """)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/optional-client-scopes/offline-scope-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("[]")),
            Expect(HttpMethod.Post, "/auth/admin/realms/ISLAMU/clients", _ => new HttpResponseMessage(HttpStatusCode.Created)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "api-uuid", "clientId": "islamu-event-api" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/api-uuid", _ => JsonResponse(ClientRepresentationJson("api-uuid", "islamu-event-api"))),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/api-uuid/client-secret", _ => JsonResponse("""
                { "value": "old-api-secret" }
                """)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/api-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.RealmCreated).IsTrue();
        await Assert.That(result.BlazorClientUpdated).IsTrue();
        await Assert.That(result.ApiClientUpdated).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(23);
        await Assert.That(handler.Requests[0].Authorization).IsNull();
        await Assert.That(handler.Requests.Skip(1).All(x => x.Authorization?.Scheme == "Bearer")).IsTrue();
        await Assert.That(handler.Requests.Skip(1).All(x => x.Authorization?.Parameter == "admin-token")).IsTrue();
        await Assert.That(handler.Requests[16].Body).Contains("runtime-blazor-secret");
        await Assert.That(handler.Requests[16].Body).Contains("offline_access");
        await Assert.That(handler.Requests[16].Body).Contains("use.refresh.tokens");
        await Assert.That(handler.Requests[22].Body).Contains("optional-api-secret");

        var serializedResult = JsonSerializer.Serialize(result);
        await Assert.That(serializedResult).DoesNotContain(request.BootstrapAdminPassword);
        await Assert.That(serializedResult).DoesNotContain(request.BlazorClientSecret);
        await Assert.That(serializedResult).DoesNotContain(request.ApiClientSecret!);
    }

    [Test]
    public async Task BootstrapAsync_PatchExistingRealm_UpdatesExistingClientsWithoutCreatingThem()
    {
        var request = CreateRequest(mode: KeycloakBootstrapMode.PatchExistingRealm);
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ => new HttpResponseMessage(HttpStatusCode.OK)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => JsonResponse(ClientRepresentationJson("blazor-uuid", "islamu-event-blazor"))),
            ExpectOfflineAccessRole(),
            ExpectDefaultRole(),
            ExpectDefaultRoleCompositeUpdate(),
            ExpectOfflineAccessScopeLookup(),
            ExpectOfflineAccessRole(),
            ExpectOfflineAccessScopeMappingUpdate(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/client-secret", _ => JsonResponse("""
                { "value": "old-blazor-secret" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/client-scopes", _ => JsonResponse("""
                [{ "id": "offline-scope-uuid", "name": "offline_access" }]
                """)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/optional-client-scopes/offline-scope-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "api-uuid", "clientId": "islamu-event-api" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/api-uuid", _ => JsonResponse(ClientRepresentationJson("api-uuid", "islamu-event-api"))),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/api-uuid/client-secret", _ => JsonResponse("""
                { "value": "old-api-secret" }
                """)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/api-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.RealmCreated).IsFalse();
        await Assert.That(handler.Requests.Any(x => x.Method == HttpMethod.Post
            && x.RequestUri?.AbsolutePath == "/auth/admin/realms/ISLAMU/clients")).IsFalse();
    }

    [Test]
    public async Task BootstrapAsync_PatchExistingRealmWhenSecretAlreadyMatchesAndRefreshSettingsMissing_RepairsClient()
    {
        var request = CreateRequest(mode: KeycloakBootstrapMode.PatchExistingRealm) with
        {
            ApiClientId = null,
            ApiClientSecret = null
        };
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ => new HttpResponseMessage(HttpStatusCode.OK)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => JsonResponse(ClientRepresentationJson(
                "blazor-uuid",
                "islamu-event-blazor",
                "runtime-blazor-secret",
                includeRefreshTokenSettings: false))),
            ExpectOfflineAccessRole(),
            ExpectDefaultRole(),
            ExpectDefaultRoleCompositeUpdate(),
            ExpectOfflineAccessScopeLookup(),
            ExpectOfflineAccessRole(),
            ExpectOfflineAccessScopeMappingUpdate(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/client-scopes", _ => JsonResponse("""
                [{ "id": "offline-scope-uuid", "name": "offline_access" }]
                """)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/optional-client-scopes/offline-scope-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.BlazorClientUpdated).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(13);
        await Assert.That(handler.Requests[12].Body).Contains("offline_access");
        await Assert.That(handler.Requests[12].Body).Contains("use.refresh.tokens");
        await Assert.That(handler.Requests[12].Body).DoesNotContain(request.BootstrapAdminPassword);
    }

    [Test]
    public async Task BootstrapAsync_PatchExistingRealmWhenSecretAlreadyMatchesAndRefreshSettingsPresent_DoesNotMutateClient()
    {
        var request = CreateRequest(mode: KeycloakBootstrapMode.PatchExistingRealm) with
        {
            ApiClientId = null,
            ApiClientSecret = null
        };
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ => new HttpResponseMessage(HttpStatusCode.OK)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => JsonResponse(ClientRepresentationJson(
                "blazor-uuid",
                "islamu-event-blazor",
                "runtime-blazor-secret",
                includeRefreshTokenSettings: true,
                includeAudienceMapper: true))),
            ExpectOfflineAccessRole(),
            ExpectDefaultRole(),
            ExpectDefaultRoleCompositeUpdate(),
            ExpectOfflineAccessScopeLookup(),
            ExpectOfflineAccessRole(),
            ExpectOfflineAccessScopeMappingUpdate());
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.BlazorClientUpdated).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(10);
        await Assert.That(handler.Requests.Any(x => x.Method == HttpMethod.Put)).IsFalse();
        await Assert.That(handler.Requests.Any(x => x.RequestUri?.AbsolutePath.Contains("optional-client-scopes", StringComparison.Ordinal) == true)).IsFalse();
    }

    [Test]
    public async Task BootstrapAsync_PatchExistingRealmWhenBrowserUrisMissing_AddsLoginRepairSettings()
    {
        var request = CreateRequest(mode: KeycloakBootstrapMode.PatchExistingRealm) with
        {
            ApiClientSecret = null,
            BlazorRedirectUris = ["https://localhost:7177/*"],
            BlazorWebOrigins = ["+"]
        };
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ => new HttpResponseMessage(HttpStatusCode.OK)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => JsonResponse(ClientRepresentationJson(
                "blazor-uuid",
                "islamu-event-blazor",
                "runtime-blazor-secret",
                includeRefreshTokenSettings: true))),
            ExpectOfflineAccessRole(),
            ExpectDefaultRole(),
            ExpectDefaultRoleCompositeUpdate(),
            ExpectOfflineAccessScopeLookup(),
            ExpectOfflineAccessRole(),
            ExpectOfflineAccessScopeMappingUpdate(),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(11);
        await Assert.That(handler.Requests[10].Body).Contains("https://localhost:7177/*");
        await Assert.That(handler.Requests[10].Body).Contains("\"webOrigins\":[\"\\u002B\"]");
        await Assert.That(handler.Requests[10].Body).Contains("\"post.logout.redirect.uris\":\"\\u002B\"");
        await Assert.That(handler.Requests[10].Body).Contains("islamu-event-api-audience");
        await Assert.That(handler.Requests[10].Body).Contains("\"included.client.audience\":\"islamu-event-api\"");
    }

    [Test]
    public async Task BootstrapAsync_PatchExistingRealmWhenMissing_ReturnsSafeFailureWithoutClientMutation()
    {
        var request = CreateRequest(mode: KeycloakBootstrapMode.PatchExistingRealm);
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("keycloak_realm_not_found");
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
        await Assert.That(result.Message).DoesNotContain(request.BootstrapAdminPassword);
        await Assert.That(result.Message).DoesNotContain(request.BlazorClientSecret);
    }

    [Test]
    public async Task BootstrapAsync_WhenAdminAuthenticationFails_ReturnsSafeFailureWithoutAdminCalls()
    {
        var request = CreateRequest();
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("admin-secret rejected")
            }));
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("keycloak_auth_failed");
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(result.Message).DoesNotContain("admin-secret rejected");
        await Assert.That(result.Message).DoesNotContain(request.BootstrapAdminPassword);
    }

    [Test]
    public async Task BootstrapAsync_WhenRealmLookupTransportFails_ReturnsUnreachableFailure()
    {
        var request = CreateRequest();
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ =>
                throw new HttpRequestException("one-time-admin-secret transport failure")));
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("keycloak_unreachable");
        await Assert.That(result.Message).IsEqualTo("Keycloak Admin API was unreachable during bootstrap.");
        await Assert.That(JsonSerializer.Serialize(result)).DoesNotContain(request.BootstrapAdminPassword);
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task BootstrapAsync_WhenAdminTokenTransportFails_ReturnsUnreachableFailure()
    {
        var request = CreateRequest();
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ =>
                throw new HttpRequestException("one-time-admin-secret token transport failure")));
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("keycloak_unreachable");
        await Assert.That(result.Message).IsEqualTo("Keycloak Admin API was unreachable during bootstrap.");
        await Assert.That(JsonSerializer.Serialize(result)).DoesNotContain(request.BootstrapAdminPassword);
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Arguments("http://127.0.0.1:8080")]
    [Arguments("https://localhost:8443")]
    [Arguments("ftp://keycloak.example.com")]
    [Test]
    public async Task BootstrapAsync_WithUnsafeUrl_ReturnsFailureWithoutHttpRequest(string keycloakBaseUrl)
    {
        var request = CreateRequest(keycloakBaseUrl: keycloakBaseUrl);
        var handler = new OrderedMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var service = CreateService(handler);

        var result = await service.BootstrapAsync(request, CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsNotNull();
        await Assert.That(result.FailureCode!).StartsWith("keycloak_");
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task DiagnoseRealmAsync_BasicMode_UsesOnlyOidcDiscoveryAndReturnsSafeWarning()
    {
        var configuration = CreateConfiguration();
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Get, "/auth/realms/ISLAMU/.well-known/openid-configuration", _ => JsonResponse("{}")));
        var service = CreateService(handler);

        var result = await service.DiagnoseRealmAsync(configuration, new KeycloakRealmDoctorRequestDto(), CancellationToken.None);

        await Assert.That(result.OverallStatus).IsEqualTo("needs-repair");
        await Assert.That(result.Realm).IsEqualTo("ISLAMU");
        await Assert.That(result.Checks.Any(check => check.Code == "keycloak_discovery_reachable")).IsTrue();
        await Assert.That(result.Checks.Any(check => check.Code == "keycloak_admin_credentials_required")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(handler.Requests[0].Authorization).IsNull();
        await Assert.That(handler.Requests[0].Method).IsEqualTo(HttpMethod.Get);
    }

    [Test]
    public async Task DiagnoseRealmAsync_WithTemporaryAdminCredentials_UsesReadOnlyAdminApiAndRedactsSecrets()
    {
        var configuration = CreateConfiguration();
        var request = new KeycloakRealmDoctorRequestDto
        {
            UseTemporaryAdminCredentials = true,
            BootstrapAdminUsername = "bootstrap-admin",
            BootstrapAdminPassword = "temporary-admin-secret",
            ApiClientId = "islamu-event-api"
        };
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Get, "/auth/realms/ISLAMU/.well-known/openid-configuration", _ => JsonResponse("{}")),
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ => JsonResponse("{}")),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => JsonResponse(ClientRepresentationJson(
                "blazor-uuid",
                "islamu-event-blazor",
                includeRefreshTokenSettings: true))),
            ExpectOfflineAccessRole(),
            ExpectDefaultRole(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/roles-by-id/default-role-uuid/composites/realm", _ => JsonResponse("""
                [{ "id": "offline-role-uuid", "name": "offline_access" }]
                """)),
            ExpectOfflineAccessScopeLookup(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/optional-client-scopes", _ => JsonResponse("""
                [{ "id": "offline-scope-uuid", "name": "offline_access" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/default-client-scopes", _ => JsonResponse("[]")),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/client-scopes/offline-scope-uuid/scope-mappings/realm/composite", _ => JsonResponse("""
                [{ "id": "offline-role-uuid", "name": "offline_access" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "api-uuid", "clientId": "islamu-event-api" }]
                """)));
        var service = CreateService(handler);

        var result = await service.DiagnoseRealmAsync(configuration, request, CancellationToken.None);

        await Assert.That(result.OverallStatus).IsEqualTo("healthy");
        await Assert.That(result.Checks.All(check => check.Status == "healthy")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(13);
        await Assert.That(handler.Requests.Skip(2).All(x => x.Method == HttpMethod.Get)).IsTrue();
        await Assert.That(handler.Requests.Skip(2).All(x => x.Authorization?.Scheme == "Bearer")).IsTrue();
        await Assert.That(handler.Requests.Skip(2).All(x => x.Authorization?.Parameter == "admin-token")).IsTrue();
        await Assert.That(handler.Requests.Any(x => x.Method == HttpMethod.Put || x.Method == HttpMethod.Delete)).IsFalse();

        var serializedResult = JsonSerializer.Serialize(result);
        await Assert.That(serializedResult).DoesNotContain(request.BootstrapAdminPassword);
        await Assert.That(serializedResult).DoesNotContain("admin-token");
    }

    [Test]
    public async Task PreviewRealmSyncAsync_BasicMode_ReturnsDesiredStateWithoutAdminCalls()
    {
        var configuration = CreateConfiguration();
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Get, "/auth/realms/ISLAMU/.well-known/openid-configuration", _ => JsonResponse("{}")));
        var service = CreateService(handler);

        var result = await service.PreviewRealmSyncAsync(configuration, new KeycloakRealmSyncPreviewRequestDto(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo("blocked");
        await Assert.That(result.DesiredState.DestructiveOperationsSupported).IsFalse();
        await Assert.That(result.DesiredState.Clients.Any(client => client.ClientId == "islamu-event-blazor")).IsTrue();
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-admin-credentials-required")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(handler.Requests[0].Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(handler.Requests[0].Authorization).IsNull();
    }

    [Test]
    public async Task PreviewRealmSyncAsync_WithTemporaryAdminCredentials_UsesReadOnlyAdminApiAndRedactsSecrets()
    {
        var configuration = CreateConfiguration();
        var request = new KeycloakRealmSyncPreviewRequestDto
        {
            UseTemporaryAdminCredentials = true,
            BootstrapAdminUsername = "bootstrap-admin",
            BootstrapAdminPassword = "temporary-admin-secret",
            ApiClientId = "islamu-event-api"
        };
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Get, "/auth/realms/ISLAMU/.well-known/openid-configuration", _ => JsonResponse("{}")),
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ => JsonResponse("{}")),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => JsonResponse(ClientRepresentationJson(
                "blazor-uuid",
                "islamu-event-blazor",
                includeRefreshTokenSettings: false))),
            ExpectOfflineAccessRole(),
            ExpectDefaultRole(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/roles-by-id/default-role-uuid/composites/realm", _ => JsonResponse("[]")),
            ExpectOfflineAccessScopeLookup(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/optional-client-scopes", _ => JsonResponse("[]")),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/default-client-scopes", _ => JsonResponse("[]")),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/client-scopes/offline-scope-uuid/scope-mappings/realm/composite", _ => JsonResponse("[]")),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("[]")));
        var service = CreateService(handler);

        var result = await service.PreviewRealmSyncAsync(configuration, request, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo("changes-planned");
        await Assert.That(result.RequiresBackupBeforeApply).IsTrue();
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-blazor-refresh-token-settings")).IsTrue();
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-default-role-offline-access-add")).IsTrue();
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-api-client-add")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(14);
        await Assert.That(handler.Requests.Skip(2).All(x => x.Method == HttpMethod.Get)).IsTrue();
        await Assert.That(handler.Requests.Any(x => x.Method == HttpMethod.Put || x.Method == HttpMethod.Delete)).IsFalse();

        var serializedResult = JsonSerializer.Serialize(result);
        await Assert.That(serializedResult).DoesNotContain(request.BootstrapAdminPassword);
        await Assert.That(serializedResult).DoesNotContain("admin-token");
    }

    [Test]
    public async Task ApplyRealmSyncAsync_WithoutBackupConfirmation_DoesNotContactKeycloak()
    {
        var configuration = CreateConfiguration();
        var handler = new OrderedMessageHandler(_ => throw new InvalidOperationException("Keycloak should not be contacted."));
        var service = CreateService(handler);

        var result = await service.ApplyRealmSyncAsync(configuration, new KeycloakRealmSyncApplyRequestDto(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo("blocked");
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-backup-confirmation-required")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ApplyRealmSyncAsync_WithTemporaryAdminCredentials_AppliesOnlyAdditiveAdminApiChangesAndRedactsSecrets()
    {
        var configuration = CreateConfiguration();
        var request = new KeycloakRealmSyncApplyRequestDto
        {
            BackupConfirmed = true,
            BootstrapAdminUsername = "bootstrap-admin",
            BootstrapAdminPassword = "temporary-admin-secret",
            ApiClientId = "islamu-event-api"
        };
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Get, "/auth/realms/ISLAMU/.well-known/openid-configuration", _ => JsonResponse("{}")),
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU", _ => JsonResponse("{}")),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => JsonResponse(ClientRepresentationJson(
                "blazor-uuid",
                "islamu-event-blazor",
                includeRefreshTokenSettings: false))),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)),
            ExpectOfflineAccessRole(),
            ExpectDefaultRole(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/roles-by-id/default-role-uuid/composites/realm", _ => JsonResponse("[]")),
            ExpectDefaultRoleCompositeUpdate(),
            ExpectOfflineAccessScopeLookup(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/optional-client-scopes", _ => JsonResponse("[]")),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/default-client-scopes", _ => JsonResponse("[]")),
            ExpectOfflineAccessScopeLookup(),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid/optional-client-scopes/offline-scope-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/client-scopes/offline-scope-uuid/scope-mappings/realm/composite", _ => JsonResponse("[]")),
            ExpectOfflineAccessRole(),
            ExpectOfflineAccessScopeMappingUpdate(),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("[]")),
            Expect(HttpMethod.Post, "/auth/admin/realms/ISLAMU/clients", _ => new HttpResponseMessage(HttpStatusCode.Created)));
        var service = CreateService(handler);

        var result = await service.ApplyRealmSyncAsync(configuration, request, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo("applied");
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-blazor-client-update" && operation.Status == "applied")).IsTrue();
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-default-role-offline-access-add" && operation.Status == "applied")).IsTrue();
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-api-client-add" && operation.Status == "applied")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(21);
        await Assert.That(handler.Requests.Any(x => x.Method == HttpMethod.Delete)).IsFalse();
        await Assert.That(handler.Requests.Skip(2).All(x => x.Method == HttpMethod.Get || x.Method == HttpMethod.Post || x.Method == HttpMethod.Put)).IsTrue();

        var serializedResult = JsonSerializer.Serialize(result);
        await Assert.That(serializedResult).DoesNotContain(request.BootstrapAdminPassword);
        await Assert.That(serializedResult).DoesNotContain("admin-token");
    }

    [Test]
    public async Task RotateClientSecretAsync_WhenInputsAreValid_UsesPutAndRedactsSecrets()
    {
        var configuration = CreateConfiguration();
        var request = new KeycloakClientSecretRotationRequestDto
        {
            ClientId = "islamu-event-blazor",
            SecretOwnershipMode = "application-managed",
            ConfirmApplicationManagedSecret = true,
            NewClientSecret = "replacement-client-secret",
            BootstrapAdminUsername = "bootstrap-admin",
            BootstrapAdminPassword = "temporary-admin-secret"
        };
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Get, "/auth/realms/ISLAMU/.well-known/openid-configuration", _ => JsonResponse("{}")),
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients", _ => JsonResponse("""
                [{ "id": "blazor-uuid", "clientId": "islamu-event-blazor" }]
                """)),
            Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => JsonResponse(ClientRepresentationJson(
                "blazor-uuid",
                "islamu-event-blazor",
                secret: "old-client-secret",
                includeRefreshTokenSettings: true))),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/clients/blazor-uuid", _ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        var service = CreateService(handler);

        var result = await service.RotateClientSecretAsync(configuration, request, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo("rotated");
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-client-secret-rotated" && operation.Status == "applied")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(5);
        await Assert.That(handler.Requests.Any(x => x.Method == HttpMethod.Delete)).IsFalse();
        await Assert.That(handler.Requests[4].Body).Contains(request.NewClientSecret);
        await Assert.That(handler.Requests[4].Body).DoesNotContain(request.BootstrapAdminPassword);

        var serializedResult = JsonSerializer.Serialize(result);
        await Assert.That(serializedResult).DoesNotContain(request.NewClientSecret!);
        await Assert.That(serializedResult).DoesNotContain(request.BootstrapAdminPassword!);
        await Assert.That(serializedResult).DoesNotContain("admin-token");
    }

    [Test]
    public async Task RotateClientSecretAsync_WhenTargetClientDiffers_ReturnsBlockedWithoutHttpRequest()
    {
        var configuration = CreateConfiguration();
        var request = new KeycloakClientSecretRotationRequestDto
        {
            ClientId = "unmanaged-client",
            SecretOwnershipMode = "application-managed",
            ConfirmApplicationManagedSecret = true,
            NewClientSecret = "replacement-client-secret",
            BootstrapAdminUsername = "bootstrap-admin",
            BootstrapAdminPassword = "temporary-admin-secret"
        };
        var handler = new OrderedMessageHandler(_ => throw new InvalidOperationException("Keycloak should not be contacted."));
        var service = CreateService(handler);

        var result = await service.RotateClientSecretAsync(configuration, request, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo("blocked");
        await Assert.That(result.Operations.Any(operation => operation.OperationId == "keycloak-client-secret-target-unsupported")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    private static KeycloakBootstrapService CreateService(OrderedMessageHandler handler)
    {
        return new KeycloakBootstrapService(
            new StaticHttpClientFactory(new HttpClient(handler)),
            Substitute.For<ILogger<KeycloakBootstrapService>>());
    }

    private static KeycloakBootstrapRequestDto CreateRequest(
        string keycloakBaseUrl = "https://keycloak.example.com/auth",
        KeycloakBootstrapMode mode = KeycloakBootstrapMode.PatchExistingRealm)
    {
        return new KeycloakBootstrapRequestDto
        {
            KeycloakBaseUrl = keycloakBaseUrl,
            Realm = "ISLAMU",
            BlazorClientId = "islamu-event-blazor",
            BlazorClientSecret = "runtime-blazor-secret",
            ApiClientId = "islamu-event-api",
            ApiClientSecret = "optional-api-secret",
            Mode = mode,
            BootstrapAdminUsername = "bootstrap-admin",
            BootstrapAdminPassword = "one-time-admin-secret"
        };
    }

    private static AuthProviderConfigurationDto CreateConfiguration()
    {
        return new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Keycloak,
            KeycloakAuthority = "https://keycloak.example.com/auth/realms/ISLAMU",
            KeycloakClientId = "islamu-event-blazor",
            KeycloakClientSecret = "runtime-blazor-secret"
        };
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Expect(
        HttpMethod method,
        string path,
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        return request =>
        {
            if (request.Method != method || request.RequestUri?.AbsolutePath != path)
            {
                throw new InvalidOperationException(
                    $"Expected {method} {path}, got {request.Method} {request.RequestUri?.PathAndQuery}.");
            }

            return responseFactory(request);
        };
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> ExpectOfflineAccessRole()
    {
        return Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/roles/offline_access", _ => JsonResponse("""
            { "id": "offline-role-uuid", "name": "offline_access" }
            """));
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> ExpectDefaultRole()
    {
        return Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/roles/default-roles-islamu", _ => JsonResponse("""
            { "id": "default-role-uuid", "name": "default-roles-islamu" }
            """));
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> ExpectDefaultRoleCompositeUpdate()
    {
        return Expect(
            HttpMethod.Post,
            "/auth/admin/realms/ISLAMU/roles-by-id/default-role-uuid/composites",
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> ExpectOfflineAccessScopeLookup()
    {
        return Expect(HttpMethod.Get, "/auth/admin/realms/ISLAMU/client-scopes", _ => JsonResponse("""
            [{ "id": "offline-scope-uuid", "name": "offline_access" }]
            """));
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> ExpectOfflineAccessScopeMappingUpdate()
    {
        return Expect(
            HttpMethod.Post,
            "/auth/admin/realms/ISLAMU/client-scopes/offline-scope-uuid/scope-mappings/realm",
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private static string ClientRepresentationJson(
        string id,
        string clientId,
        string? secret = null,
        bool includeRefreshTokenSettings = false,
        bool includeAudienceMapper = false)
    {
        var secretJson = secret is null
            ? string.Empty
            : $"  \"secret\": \"{secret}\",\n";

        var refreshTokenSettingsJson = includeRefreshTokenSettings
            ? """
                ,
                  "optionalClientScopes": ["offline_access"],
                  "attributes": {
                    "use.refresh.tokens": "true"
                }
                """
            : string.Empty;

        var audienceMapperJson = includeAudienceMapper
            ? """
                ,
                  "protocolMappers": [
                    {
                      "name": "islamu-event-api-audience",
                      "protocol": "openid-connect",
                      "protocolMapper": "oidc-audience-mapper",
                      "consentRequired": false,
                      "config": {
                        "included.client.audience": "islamu-event-api",
                        "id.token.claim": "false",
                        "access.token.claim": "true"
                      }
                    }
                  ]
                """
            : string.Empty;

        return """
            {
              "id": "{0}",
              "clientId": "{1}",
            {2}  "enabled": true,
              "protocol": "openid-connect",
              "publicClient": false,
              "standardFlowEnabled": true,
              "defaultClientScopes": ["openid", "profile", "email", "web-origins", "acr"]{3}{4}
            }
            """.Replace("{0}", id, StringComparison.Ordinal)
            .Replace("{1}", clientId, StringComparison.Ordinal)
            .Replace("{2}", secretJson, StringComparison.Ordinal)
            .Replace("{3}", refreshTokenSettingsJson, StringComparison.Ordinal)
            .Replace("{4}", audienceMapperJson, StringComparison.Ordinal);
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class OrderedMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

        public OrderedMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            if (_responses.Count == 0)
                throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri?.PathAndQuery}.");

            return _responses.Dequeue()(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        AuthenticationHeaderValue? Authorization,
        string Body);
}
