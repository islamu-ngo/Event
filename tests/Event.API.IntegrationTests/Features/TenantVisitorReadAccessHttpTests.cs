using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class TenantVisitorReadAccessHttpTests
{
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    [Arguments("/api/Event")]
    [Arguments("/api/PublicExperience/settings")]
    [Arguments("/api/PublicExperience/shell")]
    [Arguments("/api/public-experience/home")]
    [Arguments("/api/Event/public/private-ABCDEFGH")]
    [Arguments("/api/Event/public/private-ABCDEFGH/og-image")]
    [Arguments("/api/Event/018e4e5c-7f00-7000-8000-000000000002/calendar")]
    [Arguments("/api/StorageObject/018e4e5c-7f00-7000-8000-000000000002/public")]
    public async Task AnonymousPrivateReadDeniesBeforeAnyTenantPayload(string path)
    {
        await using var factory = await CreateReadyAsync();
        await SetModeAsync(factory, "AuthenticatedOnly");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(path, Token);

        await AssertDenialAsync(response, HttpStatusCode.Unauthorized, "tenant_authentication_required");
    }

    [Test]
    [Arguments("/api/Event")]
    [Arguments("/api/PublicExperience/settings")]
    [Arguments("/api/PublicExperience/shell")]
    [Arguments("/api/public-experience/home")]
    public async Task PrivacyCommitDeniesWarmOutputAndConditionalReplay(string path)
    {
        await using var factory = await CreateReadyAsync();
        using var client = CreateClient(factory);
        using var warm = await client.GetAsync(path, Token);
        await Assert.That(warm.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await SetModeAsync(factory, "AuthenticatedOnly");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.IfNoneMatch.ParseAdd(warm.Headers.ETag?.ToString() ?? "*");

        using var response = await client.SendAsync(request, Token);

        await AssertDenialAsync(response, HttpStatusCode.Unauthorized, "tenant_authentication_required");
    }

    [Test]
    public async Task MalformedPersistedModeCannotFallBackToPublicReads()
    {
        await using var factory = await CreateReadyAsync();
        await SetModeAsync(factory, "invalid-mode");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/Event", Token);

        await AssertDenialAsync(response, HttpStatusCode.ServiceUnavailable, "tenant_read_policy_unavailable");
    }

    [Test]
    public async Task NativeEventListCannotBypassHttpReadAdmission()
    {
        await using var factory = await CreateReadyAsync();
        await SetModeAsync(factory, "AuthenticatedOnly");
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var query = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetEventListRequest, PaginatedResult<EventListDto>>>();

        var denial = await Assert.ThrowsAsync<TenantVisitorReadAccessException>(
            () => query.QueryAsync(new GetEventListRequest(), Token));

        await Assert.That(denial!.Decision).IsEqualTo(VisitorTenantReadAccessDecision.AuthenticationRequired);
    }

    [Test]
    public async Task AnonymousMcpTransportCannotDisclosePrivateTenantMetadata()
    {
        await using var factory = await CreateReadyAsync();
        var settings = factory.Services.GetRequiredService<IOptions<McpAdapterSettings>>().Value;
        await Assert.That(settings.Enabled).IsTrue();
        await SetModeAsync(factory, "AuthenticatedOnly");
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        using var response = await client.PostAsJsonAsync(settings.EndpointPath,
            new { jsonrpc = "2.0", id = 1, method = "tools/list" }, Token);

        await AssertDenialAsync(response, HttpStatusCode.Unauthorized, "tenant_authentication_required");
    }

    [Test]
    public async Task PrivateTenantStillAllowsExistingLocalLoginAndAuthenticatedReads()
    {
        await using var factory = await CreateReadyAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        await SetModeAsync(factory, "AuthenticatedOnly");
        using var client = CreateClient(factory);
        using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var session = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token),
            cancellationToken: Token);
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", session.RootElement.GetProperty("token").GetString());

        using var response = await client.GetAsync("/api/Event", Token);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var sitemap = await client.GetAsync("/sitemap.xml", Token);
        await AssertDenialAsync(sitemap, HttpStatusCode.Unauthorized, "tenant_authentication_required");
    }

    [Test]
    [Arguments("exact", HttpStatusCode.OK)]
    [Arguments("wrong", HttpStatusCode.NotFound)]
    [Arguments("member", HttpStatusCode.NotFound)]
    public async Task UnpublishedTenantBootstrapPreservesOnlyExactAdministratorAuthority(
        string authority, HttpStatusCode expected)
    {
        await using var factory = await CreateReadyAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        await using (var database = factory.CreateDatabase())
        {
            database.BindTenantLease();
            var user = await database.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier, Token);
            Guid tenantId = authority == "wrong" ? Guid.CreateVersion7() : PlatformDefaults.DefaultTenantId;
            if (authority == "wrong")
                database.Tenants.Add(new Tenant
                {
                    Id = tenantId,
                    FullName = "Other directory",
                    Slug = $"other-{tenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                });
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                UserId = user.Id,
                User = user,
                StatusId = (int)TenantUserStatusEnum.Active
            };
            database.TenantUsers.Add(membership);
            if (authority != "member")
                database.Set<TenantUserRoleGrant>().Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    TenantUserId = membership.Id,
                    TenantUser = membership,
                    Role = null!,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow
                });
            await database.SaveChangesAsync(Token);
        }
        using var client = CreateClient(factory);
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token))
        {
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var session = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token),
                cancellationToken: Token);
            client.DefaultRequestHeaders.Authorization =
                new("Bearer", session.RootElement.GetProperty("token").GetString());
        }
        await using (var database = factory.CreateDatabase())
        {
            database.BindTenantLease();
            var tenant = await database.Tenants.SingleAsync(row => row.Id == PlatformDefaults.DefaultTenantId, Token);
            tenant.TenantStatusId = (int)TenantStatusEnum.Provisioning;
            await database.SaveChangesAsync(Token);
        }

        string[] paths = ["/api/user", "/api/user/admin-authority",
            "/api/tenant/settings/documents/directory-operator-identity"];
        foreach (string path in paths)
        {
            using var response = await client.GetAsync(path, Token);
            await Assert.That(response.StatusCode).IsEqualTo(expected);
        }
        using var publicRead = await client.GetAsync("/api/PublicExperience/settings", Token);
        await Assert.That(publicRead.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PrivateSitemapCannotReplayAnonymousMachineProjection()
    {
        await using var factory = await CreateReadyAsync();
        using var client = CreateClient(factory);
        using var warm = await client.GetAsync("/sitemap.xml", Token);
        await Assert.That(warm.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await SetModeAsync(factory, "AuthenticatedOnly");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/sitemap.xml");
        request.Headers.IfNoneMatch.ParseAdd(warm.Headers.ETag?.ToString() ?? "*");

        using var response = await client.SendAsync(request, Token);

        await AssertDenialAsync(response, HttpStatusCode.Unauthorized, "tenant_authentication_required");
    }

    private static async Task<LocalAdmissionWebApplicationFactory> CreateReadyAsync()
    {
        var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var database = factory.CreateDatabase();
        database.BindTenantLease();
        database.Add(TenantDirectoryOperatorIdentityDocumentDefaults.Create(PlatformDefaults.DefaultTenantId,
            new TenantDirectoryOperatorIdentitySettings
            {
                PublicName = "Read policy operator",
                LegalName = "Read policy operator ASBL",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE",
                PublicContactEmail = "operator@example.test",
                LegalNoticeUrl = "https://example.test/legal",
                TermsUrl = "https://example.test/terms",
                PrivacyUrl = "https://example.test/privacy"
            }));
        await database.SaveChangesAsync(Token);
        return factory;
    }

    private static async Task SetModeAsync(LocalAdmissionWebApplicationFactory factory, string mode)
    {
        await using var database = factory.CreateDatabase();
        database.BindTenantLease();
        string key = GovernanceSettingKeys.PublicExperience.VisitorAccessMode;
        var setting = await database.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key, Token);
        if (setting is null)
        {
            database.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = JsonSerializer.Serialize(mode),
                Category = "VisitorTest",
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = JsonSerializer.Serialize(mode);
        }
        await database.SaveChangesAsync(Token);
    }

    private static HttpClient CreateClient(LocalAdmissionWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static async Task AssertDenialAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Token),
            cancellationToken: Token);
        await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
    }
}
