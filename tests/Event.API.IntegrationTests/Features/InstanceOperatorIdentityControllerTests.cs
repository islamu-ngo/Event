using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Explore.Application.Authentication;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public class InstanceOperatorIdentityControllerTests
{
    private const string BaseUrl = "/api/instance-operator-identity";
    private static string SetupSecret => OnboardingWebApplicationFactory.SetupSecret;

    private static async Task<Guid> SeedPlatformAdminAsync(OnboardingWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        var userId = Guid.CreateVersion7();
        dbContext.Users.Add(new User
        {
            Id = userId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            Pii = new UserPii
            {
                UserId = userId,
                Email = $"{userId:N}@integration.test",
                FirstName = "Instance",
                LastName = "Admin"
            }
        });

        dbContext.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
            AuthenticationProvider = null!,
            ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                OnboardingWebApplicationFactory.Issuer,
                userId.ToString("D")).Value,
            ProviderDisplayName = "keycloak",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        });

        var platformRole = await dbContext.Roles.SingleOrDefaultAsync(r => r.Id == (int)RoleEnum.Admin);
        if (platformRole is null)
        {
            platformRole = new Role
            {
                Id = (int)RoleEnum.Admin,
                MasterCode = "platform.admin",
                FullName = "Platform Administrator",
                Scope = RoleScopeEnum.Platform,
                RoleScope = null!,
                IsSystem = true
            };
            dbContext.Roles.Add(platformRole);
        }

        dbContext.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = null!,
            RoleId = platformRole.Id,
            Role = platformRole,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = userId
        });

        await dbContext.SaveChangesAsync();
        return userId;
    }

    [Test]
    public async Task Get_Anonymous_ReturnsUnauthorized()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(BaseUrl);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Get_TenantOnlyUser_ReturnsForbidden()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Regular User",
                ("iss", OnboardingWebApplicationFactory.Issuer),
                ("idp", "keycloak")));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Get_WithSetupSecret_PendingSetup_ReturnsOkWithUpdateLink()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        request.Headers.Add("X-Setup-Secret", SetupSecret);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(json.TryGetProperty("_links", out var links)).IsTrue();
        await Assert.That(links.TryGetProperty("update", out _)).IsTrue();
    }

    [Test]
    public async Task Get_WithInstanceAdminClaim_ReturnsOkWithUpdateLink()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();
        var userId = await SeedPlatformAdminAsync(factory);

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Instance Admin",
                ("iss", OnboardingWebApplicationFactory.Issuer),
                ("idp", "keycloak")));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(json.TryGetProperty("_links", out var links)).IsTrue();
        await Assert.That(links.TryGetProperty("update", out _)).IsTrue();
    }

    [Test]
    public async Task Put_PendingSetup_AcceptsPartialDraft()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();

        var draft = new SaveInstanceOperatorIdentityRequestDto
        {
            PublicName = "Draft Operator",
            LegalName = "Draft Legal Entity LLC"
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl);
        request.Headers.Add("X-Setup-Secret", SetupSecret);
        request.Content = JsonContent.Create(draft);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        await Assert.That(body.Id!.PublicDisclosure.IsReady).IsFalse();
        await Assert.That(body.Id.PaidCommerce.IsReady).IsFalse();
        await Assert.That(body.Id.Revision).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Put_StaleRevision_ReturnsConflict409()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();

        // 1. Initial save to establish revision
        var draft1 = new SaveInstanceOperatorIdentityRequestDto
        {
            PublicName = "Operator Rev 1"
        };
        using var req1 = new HttpRequestMessage(HttpMethod.Put, BaseUrl);
        req1.Headers.Add("X-Setup-Secret", SetupSecret);
        req1.Content = JsonContent.Create(draft1);
        var resp1 = await client.SendAsync(req1);
        var body1 = await resp1.Content.ReadFromJsonAsync<BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>>();
        Guid rev1 = body1!.Id!.Revision;

        // 2. Second save rotates revision
        var draft2 = new SaveInstanceOperatorIdentityRequestDto
        {
            ExpectedRevision = rev1,
            PublicName = "Operator Rev 2"
        };
        using var req2 = new HttpRequestMessage(HttpMethod.Put, BaseUrl);
        req2.Headers.Add("X-Setup-Secret", SetupSecret);
        req2.Content = JsonContent.Create(draft2);
        var resp2 = await client.SendAsync(req2);
        await Assert.That(resp2.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // 3. Stale update attempting to use rev1
        var draftStale = new SaveInstanceOperatorIdentityRequestDto
        {
            ExpectedRevision = rev1,
            PublicName = "Operator Stale Attempt"
        };
        using var reqStale = new HttpRequestMessage(HttpMethod.Put, BaseUrl);
        reqStale.Headers.Add("X-Setup-Secret", SetupSecret);
        reqStale.Content = JsonContent.Create(draftStale);
        var respStale = await client.SendAsync(reqStale);

        await Assert.That(respStale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Put_CompletedBootstrap_RejectsIncompleteReplacementWithBadRequest400()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();
        var userId = await SeedPlatformAdminAsync(factory);

        // Mark bootstrap as completed and save complete initial identity
        Guid initialRev;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var initialSettings = new InstanceOperatorIdentitySettings
            {
                OperatorId = Guid.CreateVersion7(),
                PublicName = "Official Operator",
                LegalName = "Official Operator LLC",
                OperatorKindCode = "nonprofit",
                JurisdictionCountryCode = "BE",
                RegistrationIdentifier = "BE0123456789",
                PublicContactEmail = "contact@example.org",
                WebsiteUrl = "https://example.org",
                LegalNoticeUrl = "https://example.org/legal",
                TermsUrl = "https://example.org/terms",
                PrivacyUrl = "https://example.org/privacy",
                OfficialOrigin = "https://example.org",
                Revision = Guid.CreateVersion7()
            };
            initialRev = initialSettings.Revision.Value;

            await dbContext.SystemSettings.AddAsync(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = "instance.operator_identity",
                Value = JsonSerializer.Serialize(initialSettings),
                ValueType = SettingValueType.Json,
                CreatedAt = DateTime.UtcNow
            });

            var completedState = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(),
                DeploymentMode.SingleTenant,
                DateTime.UtcNow);
            completedState.CompleteInteractive(userId, DateTime.UtcNow);
            await dbContext.InstanceBootstrapStates.AddAsync(completedState);

            await dbContext.SaveChangesAsync();
        }

        // Try to replace with incomplete draft as instance admin
        var incompleteDraft = new SaveInstanceOperatorIdentityRequestDto
        {
            ExpectedRevision = initialRev,
            PublicName = "Incomplete Name Only"
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl);
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Instance Admin",
                ("iss", OnboardingWebApplicationFactory.Issuer),
                ("idp", "keycloak")));
        request.Content = JsonContent.Create(incompleteDraft);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Status).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task Put_CompletedBootstrap_WithSetupSecret_ReturnsGone410()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();
        var userId = await SeedPlatformAdminAsync(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var completedState = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(),
                DeploymentMode.SingleTenant,
                DateTime.UtcNow);
            completedState.CompleteInteractive(userId, DateTime.UtcNow);
            await dbContext.InstanceBootstrapStates.AddAsync(completedState);
            await dbContext.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl);
        request.Headers.Add("X-Setup-Secret", SetupSecret);
        request.Content = JsonContent.Create(new SaveInstanceOperatorIdentityRequestDto { PublicName = "Operator" });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }
}
