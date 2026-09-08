using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// End-to-end authorization pipeline tests that validate the complete chain:
/// Keycloak → API auth middleware → IAuthorizationProvider → Cerbos PDP → response.
///
/// Uses a <see cref="RoleAwareCerbosProvider"/> that queries the containerized Cerbos
/// HTTP API with role-aware principals, exercising real policy decisions for each user.
/// </summary>
[Category(TestCategories.Security)]
[ClassDataSource<SecurityInfrastructureFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public class AuthorizationPipelineIntegrationTests : IAsyncDisposable
{
    private readonly SecurityInfrastructureFixture _infra;

    private readonly SecurityWebApplicationFactory _instanceAdminFactory;
    private readonly HttpClient _instanceAdminClient;
    private readonly SecurityWebApplicationFactory _instanceAdminControlPlaneFactory;
    private readonly HttpClient _instanceAdminControlPlaneClient;

    private readonly SecurityWebApplicationFactory _regularUserFactory;
    private readonly HttpClient _regularUserClient;
    private readonly SecurityWebApplicationFactory _regularUserControlPlaneFactory;
    private readonly HttpClient _regularUserControlPlaneClient;

    private readonly SecurityWebApplicationFactory _tenantAdminFactory;
    private readonly HttpClient _tenantAdminClient;

    private readonly HttpClient _cerbosHttpClient;

    public AuthorizationPipelineIntegrationTests(SecurityInfrastructureFixture infra)
    {
        _infra = infra;
        _cerbosHttpClient = new HttpClient { BaseAddress = new Uri(infra.CerbosHttpEndpoint) };

        var instanceAdminProvider = new RoleAwareCerbosProvider(
            infra.CerbosHttpEndpoint,
            isInstanceAdmin: true,
            tenantMemberships: new Dictionary<string, string>(),
            orgMemberships: new Dictionary<string, string>());

        _instanceAdminFactory = new SecurityWebApplicationFactory(
            infra.KeycloakAuthority,
            infra.KeycloakMetadataAddress,
            infra.CerbosGrpcEndpoint)
        {
            AuthorizationProviderOverride = instanceAdminProvider
        };
        _instanceAdminClient = _instanceAdminFactory.CreateClient();

        _instanceAdminControlPlaneFactory = CreateMultiTenantFactory(
            new RoleAwareCerbosProvider(
                infra.CerbosHttpEndpoint,
                isInstanceAdmin: true,
                tenantMemberships: new Dictionary<string, string>(),
                orgMemberships: new Dictionary<string, string>()));
        _instanceAdminControlPlaneClient = _instanceAdminControlPlaneFactory.CreateClient();

        var regularUserProvider = new RoleAwareCerbosProvider(
            infra.CerbosHttpEndpoint,
            isInstanceAdmin: false,
            tenantMemberships: new Dictionary<string, string>(),
            orgMemberships: new Dictionary<string, string>());

        _regularUserFactory = new SecurityWebApplicationFactory(
            infra.KeycloakAuthority,
            infra.KeycloakMetadataAddress,
            infra.CerbosGrpcEndpoint)
        {
            AuthorizationProviderOverride = regularUserProvider
        };
        _regularUserClient = _regularUserFactory.CreateClient();

        _regularUserControlPlaneFactory = CreateMultiTenantFactory(
            new RoleAwareCerbosProvider(
                infra.CerbosHttpEndpoint,
                isInstanceAdmin: false,
                tenantMemberships: new Dictionary<string, string>(),
                orgMemberships: new Dictionary<string, string>()));
        _regularUserControlPlaneClient = _regularUserControlPlaneFactory.CreateClient();

        var tenantAdminProvider = new RoleAwareCerbosProvider(
            infra.CerbosHttpEndpoint,
            isInstanceAdmin: false,
            tenantMemberships: new Dictionary<string, string> { ["tenant-1"] = "admin" },
            orgMemberships: new Dictionary<string, string>());

        _tenantAdminFactory = CreateMultiTenantFactory(tenantAdminProvider);
        _tenantAdminClient = _tenantAdminFactory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _instanceAdminClient.Dispose();
        _instanceAdminControlPlaneClient.Dispose();
        _regularUserClient.Dispose();
        _regularUserControlPlaneClient.Dispose();
        _tenantAdminClient.Dispose();
        _cerbosHttpClient.Dispose();
        await _instanceAdminFactory.DisposeAsync();
        await _instanceAdminControlPlaneFactory.DisposeAsync();
        await _regularUserFactory.DisposeAsync();
        await _regularUserControlPlaneFactory.DisposeAsync();
        await _tenantAdminFactory.DisposeAsync();
    }

    #region Instance Admin — Full Access

    [Test]
    public async Task InstanceAdmin_GetEvents_ShouldReturnOk()
    {
        var token = await _infra.TokenClient.GetAdminTokenAsync();
        using var request = CreateGetRequest("/api/event", token);

        var response = await _instanceAdminClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task InstanceAdmin_GetControlPlaneTenantListAndDetail_ShouldPassAuthorization()
    {
        var token = await _infra.TokenClient.GetAdminTokenAsync();

        using var listRequest = CreateGetRequest("/api/admin/control-plane/tenants", token);
        using var detailRequest = CreateGetRequest($"/api/admin/control-plane/tenants/{Guid.NewGuid()}", token);
        var listResponse = await _instanceAdminControlPlaneClient.SendAsync(listRequest);
        var detailResponse = await _instanceAdminControlPlaneClient.SendAsync(detailRequest);

        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    #endregion

    #region Regular User — Read Access

    [Test]
    public async Task RegularUser_GetEvents_ShouldReturnOk()
    {
        var token = await _infra.TokenClient.GetUserTokenAsync();
        using var request = CreateGetRequest("/api/event", token);

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task RegularUser_GetControlPlaneTenantListAndDetail_ShouldReturnForbidden()
    {
        var token = await _infra.TokenClient.GetUserTokenAsync();

        using var listRequest = CreateGetRequest("/api/admin/control-plane/tenants", token);
        using var detailRequest = CreateGetRequest($"/api/admin/control-plane/tenants/{Guid.NewGuid()}", token);
        var listResponse = await _regularUserControlPlaneClient.SendAsync(listRequest);
        var detailResponse = await _regularUserControlPlaneClient.SendAsync(detailRequest);

        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_GetControlPlaneTenantListAndDetail_ShouldReturnForbidden()
    {
        var token = await _infra.TokenClient.GetTenantAdminTokenAsync();

        using var listRequest = CreateGetRequest("/api/admin/control-plane/tenants", token);
        using var detailRequest = CreateGetRequest($"/api/admin/control-plane/tenants/{Guid.NewGuid()}", token);
        var listResponse = await _tenantAdminClient.SendAsync(listRequest);
        var detailResponse = await _tenantAdminClient.SendAsync(detailRequest);

        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Unauthenticated — Public Read, Denied Write

    [Test]
    public async Task Unauthenticated_GetEvents_ShouldReturnOk()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event");

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("public GET endpoints should be accessible without authentication");
    }

    [Test]
    public async Task Unauthenticated_CreateEvent_ShouldReturnUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/event")
        {
            Content = JsonContent.Create(new { })
        };

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Unauthenticated_CreateOrganization_ShouldReturnUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization")
        {
            Content = JsonContent.Create(new
            {
                FullName = "Unauth Org",
                Email = "unauth@test.islamu.org",
                Country = "Belgium",
                City = "Brussels",
                Address = "Test Street 1",
                Postcode = 1000
            })
        };

        var response = await _regularUserClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Cerbos Decision Verification

    /// <summary>
    /// Instance administration is an oversight role, not an authoring one. The PDP lets an instance admin
    /// read and moderate any event, but authoring it still requires authority over that event, so a
    /// compromised instance account cannot quietly rewrite tenant content.
    /// </summary>
    [Test]
    public async Task CerbosPDP_InstanceAdmin_ModeratesButDoesNotAuthorEvents()
    {
        var result = await CheckCerbosDecision(
            isInstanceAdmin: true,
            tenantMemberships: new Dictionary<string, string>(),
            orgMemberships: new Dictionary<string, string>(),
            resourceKind: ResourceKinds.Event,
            actions: ["view", "moderate-heavy", "create", "update", "delete"]);

        await Assert.That(result).HasCount(5);
        await Assert.That(result["view"]).IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result["moderate-heavy"]).IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result["create"]).IsEqualTo("EFFECT_DENY");
        await Assert.That(result["update"]).IsEqualTo("EFFECT_DENY");
        await Assert.That(result["delete"]).IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task CerbosPDP_RegularUser_ShouldOnlyViewEvents()
    {
        var result = await CheckCerbosDecision(
            isInstanceAdmin: false,
            tenantMemberships: new Dictionary<string, string>(),
            orgMemberships: new Dictionary<string, string>(),
            resourceKind: ResourceKinds.Event,
            actions: ["view", "create", "update", "delete"]);

        await Assert.That(result["view"]).IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result["create"]).IsEqualTo("EFFECT_DENY");
        await Assert.That(result["update"]).IsEqualTo("EFFECT_DENY");
        await Assert.That(result["delete"]).IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task CerbosPDP_TenantAdmin_ShouldManageOwnTenantOnly()
    {
        var ownTenantResult = await CheckCerbosDecision(
            isInstanceAdmin: false,
            tenantMemberships: new Dictionary<string, string> { ["tenant-1"] = "admin" },
            orgMemberships: new Dictionary<string, string>(),
            resourceKind: ResourceKinds.Event,
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "moderate-light"]);

        await Assert.That(ownTenantResult.Values.All(v => v == "EFFECT_ALLOW")).IsTrue().Because("tenant admin should be allowed view and moderation actions on events in own tenant");

        var ownTenantMutations = await CheckCerbosDecision(
            isInstanceAdmin: false,
            tenantMemberships: new Dictionary<string, string> { ["tenant-1"] = "admin" },
            orgMemberships: new Dictionary<string, string>(),
            resourceKind: ResourceKinds.Event,
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["create", "update", "delete"]);

        await Assert.That(ownTenantMutations.Values.All(v => v == "EFFECT_DENY")).IsTrue().Because("tenant admin should be denied direct event mutations in own tenant (delegated to org admins)");

        var otherTenantResult = await CheckCerbosDecision(
            isInstanceAdmin: false,
            tenantMemberships: new Dictionary<string, string> { ["tenant-1"] = "admin" },
            orgMemberships: new Dictionary<string, string>(),
            resourceKind: ResourceKinds.Event,
            resourceAttrs: new { tenantId = "tenant-2", organizationId = "org-2" },
            actions: ["create", "update", "delete", "moderate-light"]);

        await Assert.That(otherTenantResult.Values.All(v => v == "EFFECT_DENY")).IsTrue().Because("tenant admin should be denied all mutations and moderation on events in other tenant");
    }

    [Test]
    public async Task CerbosPDP_OrgAdmin_ShouldManageOwnOrgOnly()
    {
        var ownOrgResult = await CheckCerbosDecision(
            isInstanceAdmin: false,
            tenantMemberships: new Dictionary<string, string>(),
            orgMemberships: new Dictionary<string, string> { ["org-1"] = "admin" },
            resourceKind: ResourceKinds.Event,
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create", "update"]);

        await Assert.That(ownOrgResult.Values.All(v => v == "EFFECT_ALLOW")).IsTrue();

        var otherOrgResult = await CheckCerbosDecision(
            isInstanceAdmin: false,
            tenantMemberships: new Dictionary<string, string>(),
            orgMemberships: new Dictionary<string, string> { ["org-1"] = "admin" },
            resourceKind: ResourceKinds.Event,
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-other" },
            actions: ["create", "update", "delete"]);

        await Assert.That(otherOrgResult.Values.All(v => v == "EFFECT_DENY")).IsTrue().Because("org admin should be denied mutations on events in other org");
    }

    #endregion

    #region Helpers

    private static HttpRequestMessage CreateGetRequest(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private SecurityWebApplicationFactory CreateMultiTenantFactory(IAuthorizationProvider authorizationProvider) =>
        new(
            _infra.KeycloakAuthority,
            _infra.KeycloakMetadataAddress,
            _infra.CerbosGrpcEndpoint)
        {
            AuthorizationProviderOverride = authorizationProvider,
            DeploymentMode = DeploymentMode.MultiTenant
        };

    private async Task<Dictionary<string, string>> CheckCerbosDecision(
        bool isInstanceAdmin,
        Dictionary<string, string> tenantMemberships,
        Dictionary<string, string> orgMemberships,
        string resourceKind,
        string[] actions,
        object? resourceAttrs = null)
    {
        var attrs = resourceAttrs ?? new { tenantId = "tenant-1", organizationId = "org-1" };

        var payload = new
        {
            requestId = Guid.NewGuid().ToString(),
            includeMeta = false,
            principal = new
            {
                id = "pipeline-test-principal",
                roles = new[] { "islamuevent_authenticated_user" },
                attr = new { isInstanceAdmin, tenantMemberships, orgMemberships }
            },
            resources = new[]
            {
                new
                {
                    resource = new { kind = resourceKind, id = "pipeline-test-resource", attr = attrs },
                    actions
                }
            }
        };

        var response = await _cerbosHttpClient.PostAsJsonAsync("/api/check/resources", payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results");
        var firstResult = results.EnumerateArray().First();
        var actionsResult = firstResult.GetProperty("actions");

        var map = new Dictionary<string, string>();
        foreach (var prop in actionsResult.EnumerateObject())
        {
            map[prop.Name] = prop.Value.GetString() ?? "UNKNOWN";
        }

        return map;
    }

    #endregion

    /// <summary>
    /// IAuthorizationProvider that delegates to the containerized Cerbos HTTP API
    /// with a fixed role configuration. Each instance represents one user persona
    /// (instance admin, regular user, etc.) and queries real policy decisions.
    /// </summary>
    private sealed class RoleAwareCerbosProvider : IAuthorizationProvider, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _isInstanceAdmin;
        private readonly Dictionary<string, string> _tenantMemberships;
        private readonly Dictionary<string, string> _orgMemberships;

        public RoleAwareCerbosProvider(
            string cerbosHttpEndpoint,
            bool isInstanceAdmin,
            Dictionary<string, string> tenantMemberships,
            Dictionary<string, string> orgMemberships)
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(cerbosHttpEndpoint) };
            _isInstanceAdmin = isInstanceAdmin;
            _tenantMemberships = tenantMemberships;
            _orgMemberships = orgMemberships;
        }

        public async Task<AuthorizationDecision> AuthorizeAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            var decisions = await AuthorizeBatchAsync([request], cancellationToken);
            return decisions[0];
        }

        public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests,
            CancellationToken cancellationToken = default) =>
            (await IsAllowedBatchAsync(requests, cancellationToken))
                .Select(allowed => allowed
                    ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos)
                    : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos))
                .ToArray();

        private async Task<IReadOnlyList<bool>> IsAllowedBatchAsync(
            IReadOnlyList<AuthorizationRequest> checks,
            CancellationToken cancellationToken = default)
        {
            var resources = checks.Select(c =>
            {
                var attrs = BuildResourceAttrs(c);
                return new
                {
                    resource = new { kind = c.ResourceKind, id = c.ResourceId, attr = attrs },
                    actions = new[] { c.Action }
                };
            }).ToArray();

            var payload = new
            {
                requestId = Guid.NewGuid().ToString(),
                includeMeta = false,
                principal = new
                {
                    id = "pipeline-principal",
                    roles = new[] { "islamuevent_authenticated_user" },
                    attr = (object)new
                    {
                        isInstanceAdmin = _isInstanceAdmin,
                        tenantMemberships = _tenantMemberships,
                        orgMemberships = _orgMemberships
                    }
                },
                resources
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/check/resources", payload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);
            var results = doc.RootElement.GetProperty("results");

            var boolResults = new List<bool>();
            foreach (var resultEntry in results.EnumerateArray())
            {
                var actions = resultEntry.GetProperty("actions");
                var firstAction = actions.EnumerateObject().FirstOrDefault();
                boolResults.Add(firstAction.Value.GetString() == "EFFECT_ALLOW");
            }

            return boolResults;
        }

        /// <summary>
        /// Sends the same wire attributes the production Cerbos adapter would, so this test exercises the real
        /// fact projection rather than a parallel one that could drift from it.
        /// </summary>
        private static object BuildResourceAttrs(AuthorizationRequest check)
        {
            var attributes = AuthorizationFactAttributeProjection.ToAttributes(check.Facts);
            if (attributes is null)
            {
                return new { tenantId = "tenant-1", organizationId = "org-1" };
            }

            var projected = new Dictionary<string, object>(attributes);
            if (!projected.ContainsKey("tenantId")) projected["tenantId"] = "tenant-1";
            if (!projected.ContainsKey("organizationId")) projected["organizationId"] = "org-1";
            return projected;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
