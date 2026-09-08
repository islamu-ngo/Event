using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class ExternalApiOwnerTypeIntegrationTests
{
    private const string ProbeUrl = "/api/_internal/auth-probe/secure";

    [Test]
    public async Task UserOwnerKey_InMultiTenantMode_AuthenticatesAndResolvesTenant()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        const string keyId = "user-owner-mt";
        const string secret = "user-owner-mt-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = tenantId,
                    OwnerId = ownerId,
                    OwnerType = ExternalApiKeyOwnerType.User,
                    Scopes = ["events:read", "users:read"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.AuthMethod).IsEqualTo("api_key");
        await Assert.That(body.TenantId).IsEqualTo(tenantId);
        await Assert.That(body.ApiKeyId).IsEqualTo(keyId);
    }

    [Test]
    public async Task OrganizationOwnerKey_InMultiTenantMode_AuthenticatesWithOrgOwnerId()
    {
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        const string keyId = "org-owner-mt";
        const string secret = "org-owner-mt-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = tenantId,
                    OwnerId = orgId,
                    OwnerType = ExternalApiKeyOwnerType.Organization,
                    Scopes = ["organizations:read", "events:write"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.AuthMethod).IsEqualTo("api_key");
        await Assert.That(body.TenantId).IsEqualTo(tenantId);
        await Assert.That(body.ApiKeyId).IsEqualTo(keyId);
    }

    [Test]
    public async Task GroupOwnerKey_InMultiTenantMode_AuthenticatesWithGroupOwnerId()
    {
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        const string keyId = "group-owner-mt";
        const string secret = "group-owner-mt-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = tenantId,
                    OwnerId = groupId,
                    OwnerType = ExternalApiKeyOwnerType.Group,
                    Scopes = ["groups:read", "groups:write"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.AuthMethod).IsEqualTo("api_key");
        await Assert.That(body.TenantId).IsEqualTo(tenantId);
        await Assert.That(body.ApiKeyId).IsEqualTo(keyId);
    }

    [Test]
    public async Task TenantOwnerKey_InMultiTenantMode_AuthenticatesWithAdminTenantScope()
    {
        var tenantId = Guid.NewGuid();
        const string keyId = "tenant-owner-mt";
        const string secret = "tenant-owner-mt-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = tenantId,
                    OwnerId = tenantId,
                    OwnerType = ExternalApiKeyOwnerType.Tenant,
                    Scopes = ["admin:tenant"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.AuthMethod).IsEqualTo("api_key");
        await Assert.That(body.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task InstanceAdminOwnerKey_InMultiTenantMode_BindsExplicitTenantHint()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        const string keyId = "instance-admin-mt";
        const string secret = "instance-admin-mt-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["alpha"] = tenantId
            },
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = null,
                    OwnerId = ownerId,
                    OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                    Scopes = ["admin:instance"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);
        request.Headers.Add("X-Tenant-Slug", "alpha");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.AuthMethod).IsEqualTo("api_key");
        await Assert.That(body.ApiKeyId).IsEqualTo(keyId);
        await Assert.That(body.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task InstanceAdminOwnerKey_InMultiTenantMode_WithoutTenantHint_FailsClosed()
    {
        var ownerId = Guid.NewGuid();
        const string keyId = "instance-admin-mt-no-hint";
        const string secret = "instance-admin-mt-no-hint-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = null,
                    OwnerId = ownerId,
                    OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                    Scopes = ["admin:instance"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(body).Contains("tenant_required");
        await Assert.That(body).DoesNotContain(rawKey);
    }

    [Test]
    public async Task InstanceAdminOwnerKey_DoesNotPersistTenantBindingInCredential()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        const string keyId = "instance-admin-no-tenant";
        const string secret = "instance-admin-no-tenant-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["alpha"] = tenantId
            },
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = null,
                    OwnerId = ownerId,
                    OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                    Scopes = ["admin:instance"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);
        request.Headers.Add("X-Tenant-Slug", "alpha");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await dbContext.ExternalApiKeys.IgnoreQueryFilters().SingleAsync(k => k.KeyId == keyId);
        await Assert.That(stored.TenantId).IsNull();
    }

    [Test]
    public async Task UserOwnerKey_InSingleTenantMode_ResolvesDefaultTenant()
    {
        var ownerId = Guid.NewGuid();
        const string keyId = "user-owner-st";
        const string secret = "user-owner-st-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    OwnerId = ownerId,
                    OwnerType = ExternalApiKeyOwnerType.User,
                    Scopes = ["events:read"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
    }

    [Test]
    public async Task InstanceAdminOwnerKey_InSingleTenantMode_ResolvesDefaultTenant()
    {
        var ownerId = Guid.NewGuid();
        const string keyId = "instance-admin-st";
        const string secret = "instance-admin-st-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = null,
                    OwnerId = ownerId,
                    OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                    Scopes = ["admin:instance"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.ApiKeyId).IsEqualTo(keyId);
        await Assert.That(body.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
    }

    [Test]
    public async Task CrossTenantApiKey_RejectedWhenTenantHintDoesNotMatch()
    {
        var keyTenantId = Guid.NewGuid();
        var hintedTenantId = Guid.NewGuid();
        const string keyId = "cross-tenant-isolated";
        const string secret = "cross-tenant-isolated-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["other"] = hintedTenantId
            },
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = keyTenantId,
                    OwnerId = Guid.NewGuid(),
                    OwnerType = ExternalApiKeyOwnerType.Organization,
                    Scopes = ["organizations:read"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);
        request.Headers.Add("X-Tenant-Slug", "other");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task JwtCallerInMultiTenantMode_StillAuthenticatesAfterApiKeyMiddlewareActive()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["alpha"] = tenantId
            }
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.CreateJwt(userId));
        request.Headers.Add("X-Tenant-Slug", "alpha");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.AuthMethod).IsEqualTo("jwt");
        await Assert.That(body.UserId).IsEqualTo(userId);
        await Assert.That(body.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task ExpiredApiKey_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        const string keyId = "expired-key";
        const string secret = "expired-key-secret";
        var rawKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = tenantId,
                    OwnerId = Guid.NewGuid(),
                    OwnerType = ExternalApiKeyOwnerType.User,
                    Scopes = ["events:read"],
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawKey);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task MalformedApiKey_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = "valid-key",
                    Secret = "valid-secret",
                    TenantId = tenantId,
                    OwnerId = Guid.NewGuid(),
                    OwnerType = ExternalApiKeyOwnerType.User,
                    Scopes = ["events:read"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", "not-a-real-key.with-some-random-garbage");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RateLimiting_WithFiveOwnerTypes_PartitionsCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var keys = new[]
        {
            (keyId: "rl-user", secret: "rl-user-secret", type: ExternalApiKeyOwnerType.User, tenant: (Guid?)tenantId),
            (keyId: "rl-org", secret: "rl-org-secret", type: ExternalApiKeyOwnerType.Organization, tenant: (Guid?)tenantId),
            (keyId: "rl-group", secret: "rl-group-secret", type: ExternalApiKeyOwnerType.Group, tenant: (Guid?)tenantId),
            (keyId: "rl-tenant", secret: "rl-tenant-secret", type: ExternalApiKeyOwnerType.Tenant, tenant: (Guid?)tenantId),
            (keyId: "rl-instance", secret: "rl-instance-secret", type: ExternalApiKeyOwnerType.InstanceAdmin, tenant: (Guid?)null)
        };

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["alpha"] = tenantId
            },
            DisableRateLimitingInTesting = false,
            GlobalRateLimitTokenLimit = 1,
            GlobalRateLimitTokensPerPeriod = 1,
            GlobalRateLimitReplenishPeriodSeconds = 60,
            PersistedApiKeys = keys.Select(k => new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
            {
                KeyId = k.keyId,
                Secret = k.secret,
                TenantId = k.tenant,
                OwnerId = Guid.NewGuid(),
                OwnerType = k.type,
                Scopes = k.type == ExternalApiKeyOwnerType.InstanceAdmin ? ["admin:instance"] : ["events:read"]
            }).ToArray()
        };

        using var client = factory.CreateClient();

        foreach (var key in keys)
        {
            var rawKey = ApiKeyHashing.FormatPersistedApiKey(key.keyId, key.secret);

            using var firstRequest = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
            firstRequest.Headers.Add("X-API-Key", rawKey);
            if (key.type == ExternalApiKeyOwnerType.InstanceAdmin)
            {
                firstRequest.Headers.Add("X-Tenant-Slug", "alpha");
            }

            var firstResponse = await client.SendAsync(firstRequest);
            await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

            using var throttledRequest = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
            throttledRequest.Headers.Add("X-API-Key", rawKey);
            if (key.type == ExternalApiKeyOwnerType.InstanceAdmin)
            {
                throttledRequest.Headers.Add("X-Tenant-Slug", "alpha");
            }

            var throttledResponse = await client.SendAsync(throttledRequest);
            await Assert.That(throttledResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        }
    }

    private sealed class AuthProbeResult
    {
        public string? AuthMethod { get; set; }

        public string? ApiKeyId { get; set; }

        public Guid? TenantId { get; set; }

        public Guid? UserId { get; set; }
    }
}
