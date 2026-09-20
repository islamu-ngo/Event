using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Features.Tenants.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class ProvisioningTenantAccessHttpTests
{
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;
    private static Guid TenantId => PlatformDefaults.DefaultTenantId;

    [Test]
    [Arguments("/api/PublicExperience/settings")]
    [Arguments("/api/PublicExperience/shell")]
    [Arguments("/api/public-experience/home")]
    [Arguments("/api/Tenant")]
    [Arguments("/api/Tenant/navigation")]
    [Arguments("/api/Event")]
    [Arguments("/api/Event/public/private-ABCDEFGH")]
    [Arguments("/api/Event/public/private-ABCDEFGH/og-image")]
    [Arguments("/api/Event/018e4e5c-7f00-7000-8000-000000000002/calendar")]
    [Arguments("/api/StorageObject/018e4e5c-7f00-7000-8000-000000000002/public")]
    public async Task FixedDefaultBindingDeniesEveryPublicSurface(string path)
    {
        await using var factory = await CreateReadyAsync();
        using var client = CreateClient(factory);
        if (path == "/api/Tenant")
            await SignInAsync(factory, client);
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        using var response = await client.GetAsync(path, Token);
        await AssertLifecycleDenialAsync(response);
    }

    [Test]
    public async Task WarmSettingsShellAndTenantOutputCannotSurviveLifecycleChange()
    {
        await using var factory = await CreateReadyAsync();
        using var client = CreateClient(factory);
        string[] paths = ["/api/PublicExperience/settings", "/api/PublicExperience/shell", "/api/public-experience/home"];
        foreach (string path in paths)
        {
            using var warm = await client.GetAsync(path, Token);
            await Assert.That(warm.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        foreach (string path in paths)
        {
            using var response = await client.GetAsync(path, Token);
            await AssertLifecycleDenialAsync(response);
        }
    }

    [Test]
    public async Task DirectIdAndListDoNotExposeDifferentProvisioningTenant()
    {
        await using var factory = await CreateReadyAsync();
        Guid privateId = Guid.CreateVersion7();
        await using (var db = factory.CreateDatabase())
        {
            db.Tenants.Add(new Tenant { Id = privateId, FullName = "Private directory", Slug = "private-directory", TenantStatusId = (int)TenantStatusEnum.Provisioning, TenantStatus = null!, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(Token);
        }
        using var client = CreateClient(factory);
        await SignInAsync(factory, client);
        using var response = await client.GetAsync($"/api/Tenant/{privateId}", Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await using var scope = factory.Services.CreateAsyncScope();
        var list = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTenantListRequest, List<TenantListDto>>>()
            .QueryAsync(new GetTenantListRequest(), Token);
        await Assert.That(list.Any(tenant => tenant.Id == privateId)).IsFalse();
    }

    [Test]
    public async Task NativeSettingsAndShellFailClosedWithCompleteIdentity()
    {
        await using var factory = await CreateReadyAsync();
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(TenantId);
        var settings = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetPublicExperienceSettingsQuery, PublicExperienceSettingsDto>>()
            .QueryAsync(new GetPublicExperienceSettingsQuery(), Token);
        var shell = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetPublicExperienceShellQuery, PublicExperienceShellDto>>()
            .QueryAsync(new GetPublicExperienceShellQuery(), Token);
        await Assert.That(settings.IsAvailable).IsFalse();
        await Assert.That(settings.UnavailableCode).IsEqualTo("tenant_lifecycle_unavailable");
        await Assert.That(shell.IsAvailable).IsFalse();
        await Assert.That(shell.DirectoryOperator).IsNull();
    }

    [Test]
    public async Task SignupDiscoveryAndNativeAllocationFailClosed()
    {
        await using var factory = await CreateReadyAsync();
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        using var client = CreateClient(factory);
        using var discovery = await client.GetAsync("/api/InstanceOnboarding/auth-provider-configuration", Token);
        await Assert.That(discovery.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = await JsonDocument.ParseAsync(await discovery.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        var visitor = json.RootElement.GetProperty("visitorAccess");
        await Assert.That(visitor.GetProperty("allowsNewNativeAllocation").GetBoolean()).IsFalse();
        await Assert.That(visitor.GetProperty("signupDestinations").GetArrayLength()).IsEqualTo(0);
        await using var scope = factory.Services.CreateAsyncScope();
        var capability = await scope.ServiceProvider.GetRequiredService<IVisitorAccessCapabilityResolver>().ResolveAsync(TenantId, Token);
        await Assert.That(capability.AllowsNewNativeAllocation).IsFalse();
        using var allocation = await client.PostAsJsonAsync($"/api/events/{Guid.CreateVersion7()}/registration-orders/guest", new { }, Token);
        await AssertLifecycleDenialAsync(allocation);
        await using var db = factory.CreateDatabase();
        await Assert.That(await db.RegistrationOrders.AnyAsync(Token)).IsFalse();
    }

    [Test]
    public async Task ExistingLocalLoginRemainsReachable()
    {
        await using var factory = await CreateReadyAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        using var client = CreateClient(factory);
        using var response = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task WarmHostLookupCannotPublishProvisioningTenant()
    {
        await using var factory = await CreateReadyAsync();
        await using (var db = factory.CreateDatabase())
        {
            (await db.InstanceBootstrapStates.SingleAsync(Token)).TransitionDeploymentMode(DeploymentMode.MultiTenant);
            db.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId, Tenant = null!,
                SettingKey = GovernanceSettingKeys.Domains.TenantCustomDomain,
                Value = JsonSerializer.Serialize("private.example.test"), CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(Token);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>().InvalidateCacheAsync();
        var cache = factory.Services.GetRequiredService<ITenantSlugCache>();
        await cache.RefreshAsync(Token);
        await Assert.That(await cache.GetTenantIdByDomainAsync("private.example.test", Token)).IsEqualTo(TenantId);
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/PublicExperience/settings");
        request.Headers.Add("X-Tenant-Slug", "local-admission");
        request.Headers.Host = "private.example.test";
        using var response = await client.SendAsync(request, Token);
        await AssertLifecycleDenialAsync(response);
    }

    [Test]
    public async Task PersistedApiKeyBindingCannotPublishProvisioningTenant()
    {
        await using var factory = await CreateReadyAsync();
        string secret = Explore.Application.Services.ApiKeyHashing.CreateSecret();
        string keyId = Guid.CreateVersion7().ToString("N");
        await using (var db = factory.CreateDatabase())
        {
            Guid ownerId = (await db.InstanceBootstrapStates.SingleAsync(Token)).CompletedByUserId!.Value;
            db.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId, Name = "Lifecycle key", KeyId = keyId,
                SecretHash = Explore.Application.Services.ApiKeyHashing.ComputeHash(secret), Scopes = "[\"events:read\"]",
                OwnerType = ExternalApiKeyOwnerType.User, OwnerId = ownerId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active, ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = 1, ExternalApiKeyCreditPeriod = null!, CreatedAt = DateTime.UtcNow
            });
            (await db.InstanceBootstrapStates.SingleAsync(Token)).TransitionDeploymentMode(DeploymentMode.MultiTenant);
            await db.SaveChangesAsync(Token);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>().InvalidateCacheAsync();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", Explore.Application.Services.ApiKeyHashing.FormatPersistedApiKey(keyId, secret));
        using (var active = await client.GetAsync("/api/PublicExperience/settings", Token))
            await Assert.That(active.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        using var denied = await client.GetAsync("/api/PublicExperience/settings", Token);
        await AssertLifecycleDenialAsync(denied);
    }

    [Test]
    [Arguments("exact", HttpStatusCode.OK)]
    [Arguments("wrong", HttpStatusCode.NotFound)]
    [Arguments("member", HttpStatusCode.NotFound)]
    public async Task PrivateIdentityManagementRequiresExactDatabaseAuthority(string authority, HttpStatusCode expected)
    {
        await using var factory = await CreateReadyAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        await using (var db = factory.CreateDatabase())
        {
            var user = await db.Users.SingleAsync(user => user.Pii!.Email == credentials.Identifier, Token);
            Guid targetId = TenantId;
            if (authority == "wrong")
            {
                targetId = Guid.CreateVersion7();
                db.Tenants.Add(new Tenant { Id = targetId, FullName = "Other directory", Slug = "other-directory", TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!, CreatedAt = DateTime.UtcNow });
            }
            var membership = new TenantUser { Id = Guid.CreateVersion7(), TenantId = targetId, Tenant = null!, UserId = user.Id, User = user, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow };
            db.TenantUsers.Add(membership);
            if (authority != "member")
                db.Set<TenantUserRoleGrant>().Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(), TenantId = targetId, Tenant = null!, Role = null!, TenantUserId = membership.Id, TenantUser = membership,
                    RoleId = (int)RoleEnum.TenantAdmin, RoleScopeId = (int)RoleScopeEnum.Tenant, GrantedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync(Token);
        }
        using var client = CreateClient(factory);
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token))
        {
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        }
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        using var response = await client.GetAsync("/api/tenant/settings/documents/directory-operator-identity", Token);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        if (expected == HttpStatusCode.NotFound)
            await AssertLifecycleDenialAsync(response);
    }

    private static async Task<LocalAdmissionWebApplicationFactory> CreateReadyAsync()
    {
        var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var db = factory.CreateDatabase();
        db.Add(TenantDirectoryOperatorIdentityDocumentDefaults.Create(TenantId, new TenantDirectoryOperatorIdentitySettings
        {
            PublicName = "Lifecycle operator", LegalName = "Lifecycle operator ASBL",
            OperatorKindCode = "registered_organization", JurisdictionCountryCode = "BE",
            PublicContactEmail = "operator@example.test", LegalNoticeUrl = "https://example.test/legal",
            TermsUrl = "https://example.test/terms", PrivacyUrl = "https://example.test/privacy"
        }));
        await db.SaveChangesAsync(Token);
        return factory;
    }

    private static async Task SetLifecycleAsync(LocalAdmissionWebApplicationFactory factory, TenantStatusEnum status)
    {
        await using var db = factory.CreateDatabase();
        (await db.Tenants.SingleAsync(tenant => tenant.Id == TenantId, Token)).TenantStatusId = (int)status;
        await db.SaveChangesAsync(Token);
    }

    private static HttpClient CreateClient(LocalAdmissionWebApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    private static async Task SignInAsync(LocalAdmissionWebApplicationFactory factory, HttpClient client)
    {
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
    }

    private static async Task AssertLifecycleDenialAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo("tenant_lifecycle_unavailable");
    }
}
