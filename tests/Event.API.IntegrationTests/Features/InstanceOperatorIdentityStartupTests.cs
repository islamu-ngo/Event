namespace Event.Api.IntegrationTests.Features;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.DTOs.Onboarding;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[NotInParallel]
public class InstanceOperatorIdentityStartupTests
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
    public async Task CompletedBootstrap_WithMissingOrCorruptedIdentity_PermanentlyLocksSetupSecretAndReturns410()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();
        var userId = await SeedPlatformAdminAsync(factory);

        // Seed completed bootstrap state, but do NOT seed valid operator identity (simulate missing/corrupted identity)
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var completedState = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(),
                DeploymentMode.SingleTenant,
                DateTime.UtcNow);
            completedState.CompleteInteractive(userId, DateTime.UtcNow);
            await dbContext.InstanceBootstrapStates.AddAsync(completedState);

            // Corrupted JSON payload in SystemSettings
            await dbContext.SystemSettings.AddAsync(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = "instance.operator_identity",
                Value = "{ \"Corrupt\": true, \"PublicName\": null }",
                ValueType = SettingValueType.Json,
                CreatedAt = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        }

        // 1. GET with X-Setup-Secret must return 410 Gone
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        getRequest.Headers.Add("X-Setup-Secret", SetupSecret);
        var getResponse = await client.SendAsync(getRequest);
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.Gone);

        // 2. PUT with X-Setup-Secret must also return 410 Gone
        using var putRequest = new HttpRequestMessage(HttpMethod.Put, BaseUrl);
        putRequest.Headers.Add("X-Setup-Secret", SetupSecret);
        putRequest.Content = JsonContent.Create(new SaveInstanceOperatorIdentityRequestDto
        {
            PublicName = "Malicious Takeover Attempt"
        });
        var putResponse = await client.SendAsync(putRequest);
        await Assert.That(putResponse.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }

    [Test]
    public async Task StartupDecoupling_ApiBootsCleanlyWithoutInstanceOperatorIdentityConfiguration()
    {
        // OnboardingWebApplicationFactory starts with zero Instance:OperatorIdentity configured
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();

        // The API starts cleanly, and setup secret caller receives incomplete readiness rather than crashing
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        request.Headers.Add("X-Setup-Secret", SetupSecret);
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<InstanceOperatorIdentityDocumentDto>();
        await Assert.That(doc).IsNotNull();
        await Assert.That(doc!.IsReady).IsFalse();
        await Assert.That(doc.FailureCode).IsEqualTo("instance_operator_identity_missing");
    }

    [Test]
    public async Task PublicLegalDocument_WhenInstanceIdentityNotReady_Returns503Unavailable()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var now = DateTime.UtcNow;
            var document = LegalDocument.CreateDraft(
                LegalDocumentScope.Instance,
                tenantId: null,
                LegalDocumentKind.TermsOfService,
                LegalDocumentAudience.Public,
                [
                    LegalDocumentLocalizedSource.Create(
                        "en",
                        "Published Terms",
                        "Reviewed public terms.",
                        "# Terms\n\nAccountable operator: {{accountable_identity}}.")
                ],
                templateProvenance: null,
                "instance-identity:v1",
                requiresFreshAcceptance: false,
                now);
            document.SubmitForReview(now.AddMinutes(1));
            document.Approve(Guid.CreateVersion7(), "review-evidence:test", now.AddMinutes(2));
            document.Schedule(now.AddMinutes(4), now.AddMinutes(3));
            document.Publish(now.AddMinutes(4));

            dbContext.Set<LegalDocument>().Add(document);
            await dbContext.SaveChangesAsync();
        }

        // Instance operator identity is not ready in pending setup state.
        // Requesting an instance legal document must fail-closed with 503 and Cache-Control: no-store.
        var response = await client.GetAsync("/api/legal-documents/terms-of-service");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }
}
