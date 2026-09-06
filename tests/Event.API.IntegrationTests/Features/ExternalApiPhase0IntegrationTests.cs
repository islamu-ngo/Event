// ABOUTME: Integration tests for the external API Phase 0 authentication and tenant seam.
// ABOUTME: Covers direct JWT, API-key-derived tenant resolution, mismatch rejection, and single-tenant short-circuit behavior.

using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Authentication;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.ServiceDefaults.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ApiKeyHashing = Explore.Application.Services.ApiKeyHashing;

namespace Event.Api.IntegrationTests.Features;

public sealed class ExternalApiPhase0IntegrationTests
{
    private const string ProbeUrl = "/api/_internal/auth-probe/secure";

    [Test]
    public async Task SecureProbe_WithBearerToken_AndTenantSlug_ReturnsResolvedJwtContext()
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
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.AuthMethod).IsEqualTo("jwt");
        await Assert.That(body.UserId).IsEqualTo(userId);
        await Assert.That(body.TenantId).IsEqualTo(tenantId);
        await Assert.That(body.ApiKeyId).IsNull();
    }

    [Test]
    public async Task SecureProbe_WithApiKey_DerivesTenantFromKey()
    {
        var tenantId = Guid.NewGuid();
        var rawApiKey = ApiKeyHashing.CreateSecret();

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            ApiKeyClients =
            [
                new ApiKeyClientDescriptor
                {
                    KeyId = "key-alpha",
                    TenantId = tenantId,
                    OwnerType = "Organization",
                    OwnerId = Guid.NewGuid().ToString(),
                    Scopes = ["events:write"],
                    SecretHash = ApiKeyHashing.ComputeHash(rawApiKey)
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawApiKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.AuthMethod).IsEqualTo("api_key");
        await Assert.That(body.ApiKeyId).IsEqualTo("key-alpha");
        await Assert.That(body.TenantId).IsEqualTo(tenantId);
        await Assert.That(body.UserId).IsNull();
    }

    [Test]
    public async Task SecureProbe_WithPersistedApiKey_DerivesTenantFromPersistedCredential()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        const string keyId = "persisted-alpha";
        var secret = ApiKeyHashing.CreateSecret();
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

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
                    OwnerType = ExternalApiKeyOwnerType.Organization,
                    Scopes = ["events:write", "events:read"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawApiKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.AuthMethod).IsEqualTo("api_key");
        await Assert.That(body.ApiKeyId).IsEqualTo(keyId);
        await Assert.That(body.TenantId).IsEqualTo(tenantId);
        await Assert.That(body.UserId).IsNull();
    }

    [Test]
    public async Task SecureProbe_WithPersistedApiKey_UpdatesUsageMetadata()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        const string keyId = "persisted-usage";
        var secret = ApiKeyHashing.CreateSecret();
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

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
                    Scopes = ["events:read"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawApiKey);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await dbContext.ExternalApiKeys.IgnoreQueryFilters().SingleAsync(apiKey => apiKey.KeyId == keyId);

        await Assert.That(stored.LastUsedAt).IsNotNull();
    }

    [Test]
    public async Task SecureProbe_WithPersistedApiKeys_RateLimiting_IsPartitionedPerKey()
    {
        var tenantId = Guid.NewGuid();
        const string firstKeyId = "persisted-rate-limit-a";
        var firstSecret = ApiKeyHashing.CreateSecret();
        const string secondKeyId = "persisted-rate-limit-b";
        var secondSecret = ApiKeyHashing.CreateSecret();
        var firstRawApiKey = ApiKeyHashing.FormatPersistedApiKey(firstKeyId, firstSecret);
        var secondRawApiKey = ApiKeyHashing.FormatPersistedApiKey(secondKeyId, secondSecret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            DisableRateLimitingInTesting = false,
            GlobalRateLimitTokenLimit = 1,
            GlobalRateLimitTokensPerPeriod = 1,
            GlobalRateLimitReplenishPeriodSeconds = 60,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = firstKeyId,
                    Secret = firstSecret,
                    TenantId = tenantId,
                    OwnerId = Guid.NewGuid(),
                    OwnerType = ExternalApiKeyOwnerType.User,
                    Scopes = ["events:read"]
                },
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = secondKeyId,
                    Secret = secondSecret,
                    TenantId = tenantId,
                    OwnerId = Guid.NewGuid(),
                    OwnerType = ExternalApiKeyOwnerType.User,
                    Scopes = ["events:read"]
                }
            ]
        };

        using var client = factory.CreateClient();

        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        firstRequest.Headers.Add("X-API-Key", firstRawApiKey);
        var firstResponse = await client.SendAsync(firstRequest);

        using var secondRequestSameKey = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        secondRequestSameKey.Headers.Add("X-API-Key", firstRawApiKey);
        var throttledResponse = await client.SendAsync(secondRequestSameKey);

        using var thirdRequestDifferentKey = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        thirdRequestDifferentKey.Headers.Add("X-API-Key", secondRawApiKey);
        var isolatedResponse = await client.SendAsync(thirdRequestDifferentKey);

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(throttledResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(throttledResponse.Headers.Contains("Retry-After")).IsTrue();
        await Assert.That(isolatedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task SecureProbe_WithRevokedPersistedApiKey_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        const string keyId = "persisted-revoked";
        const string secret = "persisted-revoked-secret";
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

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
                    Status = ExternalApiKeyStatusEnum.Revoked,
                    Scopes = ["events:read"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawApiKey);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SecureProbe_WithApiKey_AndConflictingTenantSlug_ReturnsNotFound()
    {
        var apiKeyTenantId = Guid.NewGuid();
        var hintedTenantId = Guid.NewGuid();
        const string rawApiKey = "phase0-conflict-key";

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["beta"] = hintedTenantId
            },
            ApiKeyClients =
            [
                new ApiKeyClientDescriptor
                {
                    KeyId = "key-conflict",
                    TenantId = apiKeyTenantId,
                    OwnerType = "User",
                    OwnerId = Guid.NewGuid().ToString(),
                    Scopes = ["events:read"],
                    SecretHash = ApiKeyHashing.ComputeHash(rawApiKey)
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Add("X-API-Key", rawApiKey);
        request.Headers.Add("X-Tenant-Slug", "beta");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ExternalApiKeyManagement_WithPersistedApiKeyAndConflictingTenantSlug_ReturnsNotFoundWithoutCredentialEcho()
    {
        var apiKeyTenantId = Guid.NewGuid();
        var hintedTenantId = Guid.NewGuid();
        var keyId = ApiKeyHashing.CreateKeyId();
        var secret = ApiKeyHashing.CreateSecret();
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["beta"] = hintedTenantId
            },
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = apiKeyTenantId,
                    OwnerId = Guid.NewGuid(),
                    Scopes = ["events:read"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/externalapikey");
        request.Headers.Add("X-API-Key", rawApiKey);
        request.Headers.Add("X-Tenant-Slug", "beta");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(body).DoesNotContain(rawApiKey);
        await Assert.That(body).DoesNotContain(secret);
    }

    [Test]
    public async Task SecureProbe_InSingleTenantMode_DoesNotRequireTenantMaterial()
    {
        var userId = Guid.NewGuid();

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.CreateJwt(userId));

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(body.AuthMethod).IsEqualTo("jwt");
        await Assert.That(body.UserId).IsEqualTo(userId);
    }

    [Test]
    public async Task SecureProbe_WhenProbeDisabled_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            EnableAuthContextProbe = false
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.CreateJwt(userId));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SecureProbe_WithBearerAndApiKey_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var rawApiKey = ApiKeyHashing.CreateSecret();

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["alpha"] = tenantId
            },
            ApiKeyClients =
            [
                new ApiKeyClientDescriptor
                {
                    KeyId = "key-mixed",
                    TenantId = tenantId,
                    OwnerType = "Organization",
                    OwnerId = Guid.NewGuid().ToString(),
                    Scopes = ["events:write"],
                    SecretHash = ApiKeyHashing.ComputeHash(rawApiKey)
                }
            ]
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.CreateJwt(userId));
        request.Headers.Add("X-API-Key", rawApiKey);
        request.Headers.Add("X-Tenant-Slug", "alpha");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SecureProbe_WithBearerAndManagedControlPlaneKey_ReturnsBadRequest()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["alpha"] = tenantId
            }
        };
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            factory.CreateJwt(Guid.CreateVersion7()));
        request.Headers.Add("X-Control-Plane-Key", "managed-key.secret");
        request.Headers.Add("X-Tenant-Slug", "alpha");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SecureProbe_WithTrustedForwardedHost_ResolvesTenantFromCustomDomain()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            TrustLoopbackProxy = true,
            CustomDomainEnabled = true,
            AllowTenantCustomDomains = true,
            TenantDomainMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant.example.test"] = tenantId
            }
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.CreateJwt(userId));
        request.Headers.Add("X-Forwarded-Host", "tenant.example.test");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task SecureProbe_WithDirectHost_ResolvesTenantFromCustomDomain()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            CustomDomainEnabled = true,
            AllowTenantCustomDomains = true,
            TenantDomainMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant.example.test"] = tenantId
            }
        };

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.CreateJwt(userId));
        request.Headers.Host = "tenant.example.test";

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthProbeResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task ForwardedHeadersTrustOptions_WithoutTrustedBoundary_DisablesForwardedHeaderProcessing()
    {
        var trustOptions = new ForwardedHeadersTrustOptions
        {
            TrustLoopbackProxy = false
        };
        var forwardedHeadersOptions = new ForwardedHeadersOptions();

        trustOptions.ApplyTo(
            forwardedHeadersOptions,
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);

        await Assert.That(forwardedHeadersOptions.ForwardedHeaders).IsEqualTo(ForwardedHeaders.None);
    }

    private sealed class AuthProbeResult
    {
        public string? AuthMethod { get; set; }

        public string? ApiKeyId { get; set; }

        public Guid? TenantId { get; set; }

        public Guid? UserId { get; set; }
    }
}
