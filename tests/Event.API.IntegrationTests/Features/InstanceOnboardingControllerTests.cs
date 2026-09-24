using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Models.Common;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Keycloak;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public class InstanceOnboardingControllerTests
{
    private const string BaseUrl = "/api/instanceonboarding";
    private const string SettingsBaseUrl = "/api/instance/settings";
    private static string SetupSecret => OnboardingWebApplicationFactory.SetupSecret;
    private const string CerbosBootstrapEndpoint = "http://cerbos-bootstrap.test:3593";

    [Test]
    public async Task Journey_ProvidesOneSnapshotAndProfileSaveChangesItsGeneration()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Setup-Secret", SetupSecret);
        using var beforeResponse = await client.GetAsync($"{BaseUrl}/journey");
        await Assert.That(beforeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var before = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync());
        var snapshot = before.RootElement;
        await Assert.That(snapshot.GetProperty("state").GetString()).IsEqualTo("Available");
        await Assert.That(snapshot.GetProperty("generation").GetString()).IsNotNullOrEmpty();
        await Assert.That(snapshot.GetProperty("bootstrap").GetProperty("selectedDeploymentMode").GetString())
            .IsEqualTo(snapshot.GetProperty("preflight").GetProperty("deploymentMode").GetString());
        await Assert.That(snapshot.GetProperty("_links").TryGetProperty("refresh", out _)).IsTrue();
        await Assert.That(snapshot.GetProperty("_links").TryGetProperty("save-profile", out _)).IsTrue();
        var checks = snapshot.GetProperty("preflight").GetProperty("blockingChecks").EnumerateArray().ToArray();
        await Assert.That(checks.All(check => check.TryGetProperty("requirementCategory", out _)
            && check.TryGetProperty("remediationAuthority", out _)
            && check.TryGetProperty("restartRequired", out _)
            && check.TryGetProperty("reasonCode", out _))).IsTrue();
        using var save = CreateInstanceAdminRequest(HttpMethod.Patch, $"{BaseUrl}/profile", Guid.CreateVersion7(),
            new SelfHostOnboardingProfileDto { SiteName = "Journey profile", CanonicalUrl = "https://journey.example.org" }, true);
        using var saved = await client.SendAsync(save);
        await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var afterResponse = await client.GetAsync($"{BaseUrl}/journey");
        using var after = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync());
        await Assert.That(after.RootElement.GetProperty("profile").GetProperty("siteName").GetString()).IsEqualTo("Journey profile");
        await Assert.That(after.RootElement.GetProperty("generation").GetString())
            .IsNotEqualTo(snapshot.GetProperty("generation").GetString());
        await Assert.That(after.RootElement.GetProperty("preflight").GetProperty("blockingChecks").EnumerateArray()
            .Any(check => check.GetProperty("code").GetString() == "canonical_host")).IsFalse();
    }

    [Test]
    public async Task Journey_MissingSetupAuthority_DoesNotExposeProfile()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"{BaseUrl}/journey");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.TryGetProperty("profile", out _)).IsFalse();
    }

    [Test]
    [Arguments(true, HttpStatusCode.OK)]
    [Arguments(false, HttpStatusCode.Forbidden)]
    public async Task Journey_AuthenticatedCallerRequiresPersistedAdministratorAuthority(bool administrator, HttpStatusCode expectedStatus)
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        if (administrator)
            await EnsureInstanceAdminRoleAsync(factory, userId);
        else
            await EnsureUserExistsAsync(factory, userId);

        using var request = CreateInstanceAdminRequest(HttpMethod.Get, $"{BaseUrl}/journey", userId,
            body: null, includeSetupSecret: false);
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.TryGetProperty("profile", out _)).IsEqualTo(administrator);
        if (administrator)
        {
            await Assert.That(body.RootElement.GetProperty("bootstrap").GetProperty("isCurrentUserInstanceAdmin").GetBoolean()).IsTrue();
            await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        }
    }

    [Test]
    public async Task GetStatus_Anonymous_ShouldReturnOk()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{BaseUrl}/status");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task SaveProfile_WithActiveSetupAuthority_PersistsOnlyExistingNonSecretProfileSettings()
    {
        using var factory = CreateFactoryWithSetupSecret(new Dictionary<string, string?> { ["PublicBaseUrl"] = "" });
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var profile = new SelfHostOnboardingProfileDto
        {
            SiteName = "  Community Events  ",
            SupportEmail = "  support@example.org  ",
            CanonicalUrl = "https://Events.Example.Org/onboarding",
            Locale = " EN ",
            TimeZone = "UTC",
            Purpose = "Keep this operator note out of persisted settings."
        };

        string originalFromAddress;
        using (var baselineScope = factory.Services.CreateScope())
        {
            var baselineDbContext = baselineScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            originalFromAddress = await baselineDbContext.SystemSettings
                .Where(setting => setting.SettingKey == GovernanceSettingKeys.Email.FromAddress)
                .Select(setting => setting.Value)
                .SingleAsync();
        }

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{BaseUrl}/profile",
            userId,
            profile,
            includeSetupSecret: true);
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var settings = await dbContext.SystemSettings
            .Where(setting => setting.SettingKey == GovernanceSettingKeys.Branding.DisplayName
                || setting.SettingKey == GovernanceSettingKeys.Branding.SupportEmail
                || setting.SettingKey == GovernanceSettingKeys.Domains.PublicBaseUrl
                || setting.SettingKey == GovernanceSettingKeys.Localization.DefaultLanguage)
            .ToDictionaryAsync(setting => setting.SettingKey, setting => setting.Value);

        await Assert.That(settings[GovernanceSettingKeys.Branding.DisplayName]).IsEqualTo(JsonSerializer.Serialize("Community Events"));
        await Assert.That(settings[GovernanceSettingKeys.Branding.SupportEmail]).IsEqualTo(JsonSerializer.Serialize("support@example.org"));
        await Assert.That(settings[GovernanceSettingKeys.Domains.PublicBaseUrl]).IsEqualTo(JsonSerializer.Serialize("https://Events.Example.Org/onboarding"));
        await Assert.That(settings[GovernanceSettingKeys.Localization.DefaultLanguage]).IsEqualTo(JsonSerializer.Serialize("en"));
        await Assert.That(settings.Count).IsEqualTo(4);
        var fromAddress = await dbContext.SystemSettings
            .Where(setting => setting.SettingKey == GovernanceSettingKeys.Email.FromAddress)
            .Select(setting => setting.Value)
            .SingleAsync();
        await Assert.That(fromAddress).IsEqualTo(originalFromAddress);
    }

    [Test]
    public async Task SaveProfile_WithMissingSetupSecret_AndAuthentication_ReturnsForbiddenProblemDetails()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{BaseUrl}/profile",
            userId,
            new SelfHostOnboardingProfileDto { SiteName = "Community Events" },
            includeSetupSecret: false);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Status).IsEqualTo(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task SaveProfile_WithInvalidSetupSecret_AndAuthentication_ReturnsForbiddenProblemDetails()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{BaseUrl}/profile",
            userId,
            new SelfHostOnboardingProfileDto { SiteName = "Community Events" },
            includeSetupSecret: true);
        request.Headers.Remove("X-Setup-Secret");
        request.Headers.Add("X-Setup-Secret", "not-the-real-secret");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Status).IsEqualTo(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task SaveProfile_WithInvalidProfile_AndValidSetupSecret_ReturnsValidationProblemDetailsWithoutPersistingSettings()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        var allowedSettingKeys = new[]
        {
            GovernanceSettingKeys.Branding.DisplayName,
            GovernanceSettingKeys.Branding.SupportEmail,
            GovernanceSettingKeys.Email.FromAddress,
            GovernanceSettingKeys.Domains.InstanceBaseDomain,
            GovernanceSettingKeys.Localization.DefaultLanguage
        };

        List<Guid> existingSettingIds;
        using (var baselineScope = factory.Services.CreateScope())
        {
            var baselineDbContext = baselineScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            existingSettingIds = await baselineDbContext.SystemSettings
                .Where(setting => allowedSettingKeys.Contains(setting.SettingKey))
                .Select(setting => setting.Id)
                .ToListAsync();
        }

        var invalidProfile = new SelfHostOnboardingProfileDto
        {
            SiteName = string.Empty,
            SupportEmail = "not-an-email",
            CanonicalUrl = "not-a-valid-url",
            Locale = string.Empty,
            TimeZone = string.Empty
        };

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{BaseUrl}/profile",
            userId,
            invalidProfile,
            includeSetupSecret: true);
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Status).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(problemDetails.Errors.Count).IsGreaterThan(0);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persistedSettingIds = await dbContext.SystemSettings
            .Where(setting => allowedSettingKeys.Contains(setting.SettingKey))
            .Select(setting => setting.Id)
            .ToListAsync();

        await Assert.That(persistedSettingIds.Count).IsEqualTo(existingSettingIds.Count);
        await Assert.That(persistedSettingIds.OrderBy(id => id).SequenceEqual(existingSettingIds.OrderBy(id => id))).IsTrue();
    }

    [Test]
    public async Task SaveProfile_AdvertisesSetupSecretRateLimitProblemDetailsMetadata()
    {
        var action = typeof(InstanceOnboardingController).GetMethod(nameof(InstanceOnboardingController.SaveProfile));
        await Assert.That(action).IsNotNull();

        var rateLimit = action!.GetCustomAttribute<EnableRateLimitingAttribute>();
        await Assert.That(rateLimit).IsNotNull();
        await Assert.That(rateLimit!.PolicyName).IsEqualTo(RateLimitingExtensions.SetupSecretPolicy);

        var has429ProblemMetadata = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == StatusCodes.Status429TooManyRequests && attribute.Type == typeof(ProblemDetails));

        await Assert.That(has429ProblemMetadata).IsTrue();
    }

    [Test]
    public async Task SaveProfile_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/profile")
        {
            Content = JsonContent.Create(new SelfHostOnboardingProfileDto { SiteName = "Community Events" })
        };
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SaveProfile_AfterCompletion_ReturnsGone()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        await EnsureInstanceAdminRoleAsync(factory, userId);

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{BaseUrl}/profile",
            userId,
            new SelfHostOnboardingProfileDto { SiteName = "Community Events" },
            includeSetupSecret: true);
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }

    [Test]
    public async Task GetSystemOnboardingStatus_WithConfiguredMultiTenant_ShouldReturnPublicMode()
    {
        using var factory = CreateFactoryWithSetupSecret(new Dictionary<string, string?>
        {
            ["Deployment:Mode"] = "MultiTenant"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/system/onboarding-status");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var status = await response.Content.ReadFromJsonAsync<SystemOnboardingStatusDto>();
        await Assert.That(status).IsNotNull();
        await Assert.That(status!.RequiresOnboarding).IsTrue();
        await Assert.That(status.DeploymentMode).IsEqualTo("MultiTenant");
    }

    [Test]
    public async Task Complete_WithValidPayload_ShouldSucceedAndPersistDeploymentMode()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        var completePayload = CreateValidOnboardingRequest() with
        {
            ExpectedJourneyGeneration = await ReadGenerationAsync(client)
        };

        using var completeRequest = CreateInstanceAdminRequest(HttpMethod.Post, $"{BaseUrl}/complete", userId, completePayload, includeSetupSecret: true);
        var completeResponse = await client.SendAsync(completeRequest);

        await Assert.That(completeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(await completeResponse.Content.ReadAsStringAsync());

        var completeBody = await completeResponse.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(completeBody).IsNotNull();
        await Assert.That(completeBody!.IsSuccess).IsTrue();

        using var getRequest = CreateInstanceAdminRequest(HttpMethod.Get, $"{SettingsBaseUrl}/deployment-mode", userId, body: null, includeSetupSecret: false);
        var getResponse = await client.SendAsync(getRequest);

        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var deploymentMode = await getResponse.Content.ReadFromJsonAsync<DeploymentModeDto>(TestJsonOptions.Default);
        await Assert.That(deploymentMode).IsNotNull();
        await Assert.That(deploymentMode!.Mode).IsEqualTo(DeploymentMode.SingleTenant);
    }

    [Test]
    public async Task Complete_WithExistingUserActor_ShouldCreateTenantMembershipWithoutReinsertingActorGraph()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();
        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);
        var actorId = await EnsureUserActorExistsAsync(factory, userId);

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Post,
            $"{BaseUrl}/complete",
            userId,
            CreateValidOnboardingRequest() with { ExpectedJourneyGeneration = await ReadGenerationAsync(client) },
            includeSetupSecret: true);
        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await dbContext.Actors.CountAsync(actor => actor.Id == actorId)).IsEqualTo(1);
        await Assert.That(await dbContext.TenantUsers.AnyAsync(tenantUser =>
            tenantUser.UserId == userId && tenantUser.ActorId == actorId)).IsTrue();
    }

    [Test]
    public async Task Complete_WithSidOnlyPrincipalAndExternalLogin_ShouldRejectSessionIdAsAccountAuthority()
    {
        using var factory = CreateFactoryWithSetupSecretWithoutClaimsTransformation();
        using var client = factory.CreateClient();

        var internalUserId = Guid.CreateVersion7();
        const string providerId = "keycloak-external-subject";

        await EnsureUserExistsAsync(factory, internalUserId);
        await EnsureUserExternalLoginAsync(factory, internalUserId, "keycloak", providerId);

        using var request = CreateCustomAuthRequest(
            HttpMethod.Post,
            $"{BaseUrl}/complete",
            CreateValidOnboardingRequest(),
            includeSetupSecret: true,
            new(ClaimTypes.Name, "Sid Only User"),
            new("sid", providerId),
            new("iss", OnboardingWebApplicationFactory.Issuer),
            new("idp", "keycloak"),
            new("email", $"{internalUserId:N}@integration.test"));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await dbContext.InstanceBootstrapStates.AnyAsync()).IsFalse();
        await Assert.That(await dbContext.PlatformUserRoles.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Complete_WithUnlinkedNonGuidProviderSubject_ShouldResolveThroughProviderLinkage()
    {
        using var factory = CreateFactoryWithSetupSecretWithoutClaimsTransformation();
        using var client = factory.CreateClient();

        const string providerId = "opaque-keycloak-provider-subject";
        var email = $"{Guid.NewGuid():N}@integration.test";
        using var request = CreateCustomAuthRequest(
            HttpMethod.Post,
            $"{BaseUrl}/complete",
            CreateValidOnboardingRequest() with { ExpectedJourneyGeneration = await ReadGenerationAsync(client) },
            includeSetupSecret: true,
            new(ClaimTypes.Name, "Unlinked Bootstrap User"),
            new("sub", providerId),
            new("iss", OnboardingWebApplicationFactory.Issuer),
            new("idp", "keycloak"),
            new("email", email),
            new("email_verified", bool.TrueString));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var accountKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            OnboardingWebApplicationFactory.Issuer, providerId).Value;
        var externalLogin = await dbContext.UserExternalLogins
            .SingleAsync(candidate =>
                candidate.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak
                && candidate.ProviderKey == accountKey);
        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == externalLogin.UserId);

        await Assert.That(user.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(user.Pii.Email).IsEqualTo(email);
    }

    [Test]
    public async Task Complete_WithStableSubjectAndSessionId_ShouldPersistIssuerBoundSubjectNotSessionId()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var internalUserId = Guid.CreateVersion7();
        const string providerId = "keycloak-sid-only-subject";
        var email = $"{internalUserId:N}@integration.test";

        using var request = CreateCustomAuthRequest(
            HttpMethod.Post,
            $"{BaseUrl}/complete",
            CreateValidOnboardingRequest() with { ExpectedJourneyGeneration = await ReadGenerationAsync(client) },
            includeSetupSecret: true,
            new(ClaimTypes.Name, "Sid Only User"),
            new("internal_user_id", internalUserId.ToString()),
            new("sub", providerId),
            new("iss", OnboardingWebApplicationFactory.Issuer),
            new("sid", "separate-authentication-session"),
            new("idp", "keycloak"),
            new("email", email));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        string accountKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            OnboardingWebApplicationFactory.Issuer, providerId).Value;
        var externalLogin = await dbContext.UserExternalLogins.SingleAsync(x =>
            x.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak
            && x.ProviderKey == accountKey);
        var createdUser = await dbContext.Users
            .Include(user => user.Pii)
            .SingleAsync(x => x.Id == externalLogin.UserId);

        await Assert.That(createdUser.Id).IsNotEqualTo(internalUserId);
        await Assert.That(await dbContext.Users.AnyAsync(user => user.Id == internalUserId)).IsFalse();
        await Assert.That(createdUser.Pii.Email).IsEqualTo(email);
        await Assert.That(externalLogin.AuthenticationProviderId)
            .IsEqualTo((int)AuthenticationProviderKind.Keycloak);
        await Assert.That(externalLogin.ProviderKey).IsEqualTo(accountKey);
    }

    [Test]
    public async Task Journey_WithoutConfiguredPublicAddress_IsReadyAndDoesNotPersistAddress()
    {
        using var factory = CreateFactoryWithSetupSecret(new Dictionary<string, string?>
        {
            ["Authentication:Provider"] = "local",
            ["Authorization:Provider"] = "local",
            ["PublicBaseUrl"] = string.Empty,
            ["App:PublicBaseUrl"] = string.Empty,
            ["ASPNETCORE_URLS"] = string.Empty
        });
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        client.DefaultRequestHeaders.Add("X-Setup-Secret", SetupSecret);
        using var preflightResponse = await client.GetAsync($"{BaseUrl}/journey");
        await Assert.That(preflightResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var journey = JsonDocument.Parse(await preflightResponse.Content.ReadAsStringAsync());
        var preflight = journey.RootElement.GetProperty("preflight").Deserialize<OnboardingPreflightDto>(TestJsonOptions.Default);
        await Assert.That(preflight).IsNotNull();
        await Assert.That(preflight!.IsReadyToLaunch).IsTrue()
            .Because(string.Join(",", preflight.BlockingChecks.Where(check => check.Status != OnboardingPreflightCheckStatus.Pass).Select(check => check.Code)));
        await Assert.That(preflight.BlockingChecks.Any(check => check.Code == "canonical_host")).IsFalse();
        await Assert.That(preflight.BlockingChecks.Single(check => check.Code == "auth_config").Status)
            .IsEqualTo(OnboardingPreflightCheckStatus.Pass);

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISystemSettingRepository>();
        await Assert.That(await settings.GetByKey(GovernanceSettingKeys.Domains.PublicBaseUrl)).IsNull();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await dbContext.InstanceBootstrapStates.AnyAsync()).IsFalse();
        await Assert.That(await dbContext.PlatformUserRoles.AnyAsync(role => role.UserId == userId)).IsFalse();
        var setupSecretProvider = scope.ServiceProvider.GetRequiredService<ISetupSecretProvider>();
        await Assert.That(await setupSecretProvider.IsSetupModeActiveAsync()).IsTrue();
    }

    [Test]
    public async Task UpdateModuleSettings_WhenUserIsNotInstanceAdmin_ShouldReturnForbidden()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var nonAdminUserId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, nonAdminUserId);

        using var request = CreateInstanceAdminRequest(HttpMethod.Patch, $"{SettingsBaseUrl}/modules", nonAdminUserId,
            new PatchModuleSettingsDto
            {
                EnableIslamicModule = OptionalUpdate<bool>.Set(true),
                EnableTechModule = OptionalUpdate<bool>.Set(true)
            }, includeSetupSecret: false);
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task RetiredInstanceSettingsAndOnboardingWrites_ShouldNotBeRoutable()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();
        (HttpMethod Method, string Path, HttpStatusCode ExpectedStatus)[] retiredWrites =
        [
            (HttpMethod.Put, $"{SettingsBaseUrl}/modules", HttpStatusCode.MethodNotAllowed),
            (HttpMethod.Put, $"{BaseUrl}/auth-provider-configuration", HttpStatusCode.MethodNotAllowed),
            (HttpMethod.Put, $"{BaseUrl}/authz-provider-configuration", HttpStatusCode.NotFound)
        ];

        foreach (var (method, path, expectedStatus) in retiredWrites)
        {
            using var request = new HttpRequestMessage(method, path);
            var response = await client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
        }
    }

    [Test]
    public async Task Complete_IgnoresClientDeploymentMode_WhenNoDeploymentModeSecret_ShouldPersistSingleTenant()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        var clientPayload = CreateValidOnboardingRequest() with
        {
            ExpectedJourneyGeneration = await ReadGenerationAsync(client)
        };
        clientPayload.DeploymentMode = DeploymentMode.MultiTenant;

        using var completeRequest = CreateInstanceAdminRequest(HttpMethod.Post, $"{BaseUrl}/complete", userId, clientPayload, includeSetupSecret: true);
        var completeResponse = await client.SendAsync(completeRequest);

        await Assert.That(completeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var bootstrap = await dbContext.InstanceBootstrapStates.SingleAsync();
        await Assert.That(bootstrap.DeploymentMode).IsEqualTo(DeploymentMode.SingleTenant);
    }

    [Test]
    public async Task Complete_UsesConfiguredMultiTenantMode_WhenClientPayloadSaysSingleTenant()
    {
        using var factory = CreateFactoryWithSetupSecret(new Dictionary<string, string?>
        {
            ["Deployment:Mode"] = "MultiTenant"
        });
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        var clientPayload = new CompleteInstanceOnboardingRequest
        {
            ExpectedJourneyGeneration = await ReadGenerationAsync(client),
            DeploymentMode = DeploymentMode.SingleTenant,
            SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Integration Test Instance" }
        };

        using var completeRequest = CreateInstanceAdminRequest(HttpMethod.Post, $"{BaseUrl}/complete", userId, clientPayload, includeSetupSecret: true);
        var completeResponse = await client.SendAsync(completeRequest);

        await Assert.That(completeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var bootstrap = await dbContext.InstanceBootstrapStates.SingleAsync();
        await Assert.That(bootstrap.DeploymentMode).IsEqualTo(DeploymentMode.MultiTenant);
    }

    [Test]
    public async Task UpdateAuthProviderConfiguration_AdminEndpoint_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{SettingsBaseUrl}/auth-provider")
        {
            Content = JsonContent.Create(CreateGoogleOnlyAuthProviderPatch())
        };
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateAuthProviderConfiguration_WhenUserIsNotInstanceAdmin_ShouldReturnForbidden()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{SettingsBaseUrl}/auth-provider",
            userId,
            CreateGoogleOnlyAuthProviderPatch(),
            includeSetupSecret: false);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UpdateAuthProviderConfiguration_WhenItWouldDisableAllLinkedAdminProviders_ShouldReturnBadRequest()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);
        await EnsureInstanceAdminRoleAsync(factory, userId);

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{SettingsBaseUrl}/auth-provider",
            userId,
            CreateGoogleOnlyAuthProviderPatch(),
            includeSetupSecret: false);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        var problemDetails = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Detail).Contains("Cannot disable all authentication providers linked");
    }

    [Test]
    public async Task UpdateAuthProviderConfiguration_WhenAdminHasLinkedEnabledProvider_ShouldUpdateAndReturnConfiguration()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);
        await EnsureInstanceAdminRoleAsync(factory, userId);
        await EnsureUserExternalLoginAsync(factory, userId, "google", $"google-{userId:N}");

        using var updateRequest = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{SettingsBaseUrl}/auth-provider",
            userId,
            CreateGoogleOnlyAuthProviderPatch(),
            includeSetupSecret: false);

        var updateResponse = await client.SendAsync(updateRequest);
        await Assert.That(updateResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var updateBody = await updateResponse.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(updateBody).IsNotNull();
        await Assert.That(updateBody!.IsSuccess).IsTrue();

        using var getRequest = CreateInstanceAdminRequest(
            HttpMethod.Get,
            $"{SettingsBaseUrl}/auth-provider",
            userId,
            body: null,
            includeSetupSecret: false);

        var getResponse = await client.SendAsync(getRequest);
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var config = await getResponse.Content.ReadFromJsonAsync<AuthProviderConfigurationDto>(TestJsonOptions.Default);
        await Assert.That(config).IsNotNull();
        await Assert.That(config!.PrimaryProviderId)
            .IsEqualTo((int)AuthenticationProviderKind.Local);
        await Assert.That(config.GoogleSsoEnabled).IsTrue();
    }

    [Test]
    public async Task DeploymentKeycloakConfiguration_PublicStatusAndAdminReads_ExposeSanitizedDetectedProvider()
    {
        const string authority = "https://id.example.test/realms/events";
        const string clientId = "event-blazor";
        const string clientSecret = "must-not-leave-the-server";
        using var factory = CreateFactoryWithSetupSecret(new Dictionary<string, string?>
        {
            ["Authentication:Provider"] = "keycloak",
            ["Keycloak:Authority"] = authority,
            ["Keycloak:ClientId"] = clientId,
            ["Keycloak:ClientSecret"] = clientSecret
        });
        using var client = factory.CreateClient();

        var publicResponse = await client.GetAsync($"{BaseUrl}/auth-provider-configuration");
        await Assert.That(publicResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var publicJson = await publicResponse.Content.ReadAsStringAsync();
        await Assert.That(publicJson).DoesNotContain(clientSecret);
        var publicConfiguration = JsonSerializer.Deserialize<AuthProviderConfigurationDto>(publicJson, TestJsonOptions.Default);
        await AssertDeploymentKeycloakConfiguration(publicConfiguration, authority, clientId);

        var statusResponse = await client.GetAsync($"{SettingsBaseUrl}/auth-provider/status");
        await Assert.That(statusResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var status = await statusResponse.Content.ReadFromJsonAsync<AuthProviderConfiguredResponse>();
        await Assert.That(status).IsNotNull();
        await Assert.That(status!.Configured).IsTrue();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);
        await EnsureInstanceAdminRoleAsync(factory, userId);
        using var adminRequest = CreateInstanceAdminRequest(
            HttpMethod.Get,
            $"{SettingsBaseUrl}/auth-provider",
            userId,
            body: null,
            includeSetupSecret: false);
        var adminResponse = await client.SendAsync(adminRequest);
        await Assert.That(adminResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var adminJson = await adminResponse.Content.ReadAsStringAsync();
        await Assert.That(adminJson).DoesNotContain(clientSecret);
        var adminConfiguration = JsonSerializer.Deserialize<AuthProviderConfigurationDto>(adminJson, TestJsonOptions.Default);
        await AssertDeploymentKeycloakConfiguration(adminConfiguration, authority, clientId);
    }

    [Test]
    public async Task GetAuthProviderConfigurationInternal_WithSetupSecret_ShouldReturnSecretsWhileAdminEndpointRedacts()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var configuration = scope.ServiceProvider.GetRequiredService<IAuthProviderConfigurationService>();
            await configuration.ApplyConfigurationAsync(CreateGoogleOnlyAuthProviderConfiguration());
        }

        using var internalWithoutSecretRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/auth-provider-configuration/internal");
        var internalWithoutSecretResponse = await client.SendAsync(internalWithoutSecretRequest);
        await Assert.That(internalWithoutSecretResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        using var internalWithSecretRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/auth-provider-configuration/internal");
        internalWithSecretRequest.Headers.Add("X-Setup-Secret", SetupSecret);
        var internalWithSecretResponse = await client.SendAsync(internalWithSecretRequest);
        await Assert.That(internalWithSecretResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var internalConfig = await internalWithSecretResponse.Content.ReadFromJsonAsync<AuthProviderConfigurationDto>(TestJsonOptions.Default);
        await Assert.That(internalConfig).IsNotNull();
        await Assert.That(internalConfig!.GoogleSsoEnabled).IsTrue();
        await Assert.That(internalConfig.GoogleClientSecret).IsEqualTo(
            CreateGoogleOnlyAuthProviderConfiguration().GoogleClientSecret);

        var userId = Guid.CreateVersion7();
        await EnsureInstanceAdminRoleAsync(factory, userId);
        using var adminGetRequest = CreateInstanceAdminRequest(
            HttpMethod.Get, $"{SettingsBaseUrl}/auth-provider", userId,
            body: null, includeSetupSecret: false);
        var adminResponse = await client.SendAsync(adminGetRequest);
        await Assert.That(adminResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var adminConfig = await adminResponse.Content.ReadFromJsonAsync<AuthProviderConfigurationDto>(TestJsonOptions.Default);
        await Assert.That(adminConfig).IsNotNull();
        await Assert.That(adminConfig!.GoogleClientSecret).IsEqualTo(string.Empty);

        using var closedSetupRequest = new HttpRequestMessage(
            HttpMethod.Get, $"{BaseUrl}/auth-provider-configuration/internal");
        closedSetupRequest.Headers.Add("X-Setup-Secret", SetupSecret);
        var closedSetupResponse = await client.SendAsync(closedSetupRequest);
        await Assert.That(closedSetupResponse.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }

    [Test]
    public async Task UpdateAuthorizationProviderConfiguration_AdminEndpoint_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{SettingsBaseUrl}/authz-provider")
        {
            Content = JsonContent.Create(CreateLocalAuthorizationProviderPatch())
        };
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateAuthorizationProviderConfiguration_WhenUserIsNotInstanceAdmin_ShouldReturnForbidden()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{SettingsBaseUrl}/authz-provider",
            userId,
            CreateLocalAuthorizationProviderPatch(),
            includeSetupSecret: false);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UpdateAuthorizationProviderConfiguration_WhenUserIsInstanceAdmin_ShouldUpdateAndReturnConfiguration()
    {
        using var factory = CreateFactoryWithSetupSecret(new Dictionary<string, string?>
        {
            ["Authorization:Provider"] = string.Empty,
            ["Cerbos:GrpcEndpoint"] = CerbosBootstrapEndpoint
        });
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);
        await EnsureInstanceAdminRoleAsync(factory, userId);

        using var updateRequest = CreateInstanceAdminRequest(
            HttpMethod.Patch,
            $"{SettingsBaseUrl}/authz-provider",
            userId,
            CreateLocalAuthorizationProviderPatch(),
            includeSetupSecret: false);

        var updateResponse = await client.SendAsync(updateRequest);
        await Assert.That(updateResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var updateBody = await updateResponse.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(updateBody).IsNotNull();
        await Assert.That(updateBody!.IsSuccess).IsTrue();

        using var getRequest = CreateInstanceAdminRequest(
            HttpMethod.Get,
            $"{SettingsBaseUrl}/authz-provider",
            userId,
            body: null,
            includeSetupSecret: false);

        var getResponse = await client.SendAsync(getRequest);
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var config = await getResponse.Content.ReadFromJsonAsync<AuthorizationProviderConfigurationDto>();
        await Assert.That(config).IsNotNull();
        await Assert.That(config!.Provider).IsEqualTo("local");
        await Assert.That(config.AuthorizationProviderManagedByDeployment).IsFalse();
        await Assert.That(config.CerbosGrpcEndpoint).IsEqualTo(CerbosBootstrapEndpoint);
    }

    [Test]
    public async Task AuthorizationProviderStatus_WhenDeploymentSelectsLocal_ExposesManagedReadyState()
    {
        using var factory = CreateFactoryWithSetupSecret(new Dictionary<string, string?>
        {
            ["Authorization:Provider"] = "local"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{SettingsBaseUrl}/authz-provider/status");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthProviderConfiguredResponse>();
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.Configured).IsTrue();
        await Assert.That(payload.AuthorizationProviderManagedByDeployment).IsTrue();
        await Assert.That(payload.AuthorizationProviderBootstrapStatus).IsEqualTo("ready");
    }

    [Test]
    public async Task GetAuthorizationProviderConfigurationInternal_WithSetupSecret_ShouldReturnConfiguration()
    {
        using var factory = CreateFactoryWithSetupSecret(new Dictionary<string, string?>
        {
            ["Authorization:Provider"] = string.Empty,
            ["Cerbos:GrpcEndpoint"] = CerbosBootstrapEndpoint
        });
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var configuration = scope.ServiceProvider.GetRequiredService<IAuthorizationProviderConfigurationService>();
            await configuration.ApplyConfigurationAsync(CreateLocalAuthorizationProviderConfiguration());
        }

        using var internalWithoutSecretRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/authz-provider-configuration/internal");
        var internalWithoutSecretResponse = await client.SendAsync(internalWithoutSecretRequest);
        await Assert.That(internalWithoutSecretResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        using var internalWithSecretRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/authz-provider-configuration/internal");
        internalWithSecretRequest.Headers.Add("X-Setup-Secret", SetupSecret);
        var internalWithSecretResponse = await client.SendAsync(internalWithSecretRequest);
        await Assert.That(internalWithSecretResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var internalConfig = await internalWithSecretResponse.Content.ReadFromJsonAsync<AuthorizationProviderConfigurationDto>();
        await Assert.That(internalConfig).IsNotNull();
        await Assert.That(internalConfig!.Provider).IsEqualTo("local");
        await Assert.That(internalConfig.AuthorizationProviderManagedByDeployment).IsFalse();
        await Assert.That(internalConfig.CerbosGrpcEndpoint).IsEqualTo(CerbosBootstrapEndpoint);
    }

    private static async Task EnsureInstanceAdminRoleAsync(AuthenticatedWebApplicationFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        await EnsureUserExistsAsync(factory, userId);
        await EnsureUserExternalLoginAsync(factory, userId, "keycloak", userId.ToString());
        if (!await dbContext.PlatformUserRoles.AnyAsync(role => role.UserId == userId
                && role.RoleId == (int)RoleEnum.Admin))
        {
            dbContext.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow,
                GrantedBy = userId
            });
        }

        var bootstrap = await dbContext.InstanceBootstrapStates
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        var completedAt = DateTime.UtcNow;
        if (bootstrap == null)
        {
            bootstrap = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(),
                DeploymentMode.SingleTenant,
                completedAt);
            dbContext.InstanceBootstrapStates.Add(bootstrap);
        }

        bootstrap.CompleteInteractive(userId, completedAt);

        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureUserExternalLoginAsync(
        AuthenticatedWebApplicationFactory factory,
        Guid userId,
        string provider,
        string providerKey)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        providerKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            provider == "google" ? "https://accounts.google.com" : OnboardingWebApplicationFactory.Issuer,
            providerKey).Value;
        var exists = await dbContext.UserExternalLogins
            .AnyAsync(x =>
                x.UserId == userId
                && x.AuthenticationProviderId == (int)provider.ParseAuthenticationProviderKind()
                && x.ProviderKey == providerKey);

        if (exists)
        {
            return;
        }

        dbContext.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = null!,
            AuthenticationProviderId = (int)provider.ParseAuthenticationProviderKind(),
            AuthenticationProvider = null!,
            ProviderKey = providerKey,
            ProviderDisplayName = provider,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureUserExistsAsync(AuthenticatedWebApplicationFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        var exists = await dbContext.Users.AnyAsync(x => x.Id == userId);
        if (exists)
        {
            return;
        }

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

        await dbContext.SaveChangesAsync();
        await EnsureUserExternalLoginAsync(factory, userId, "keycloak", userId.ToString());
    }

    private static async Task<Guid> EnsureUserActorExistsAsync(
        AuthenticatedWebApplicationFactory factory,
        Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var existingActorId = await dbContext.Actors
            .Where(actor => actor.UserId == userId)
            .Select(actor => (Guid?)actor.Id)
            .SingleOrDefaultAsync();
        if (existingActorId.HasValue)
        {
            return existingActorId.Value;
        }

        var actorId = Guid.CreateVersion7();
        dbContext.Actors.Add(new Actor
        {
            Id = actorId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = userId,
            Pii = new ActorPii
            {
                ActorId = actorId,
                DisplayName = "Instance Admin"
            },
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        });
        await dbContext.SaveChangesAsync();
        return actorId;
    }

    private static AuthenticatedWebApplicationFactory CreateFactoryWithSetupSecret(
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        return configurationOverrides is null
            ? new OnboardingWebApplicationFactory()
            : new ConfigurableAuthenticatedWebApplicationFactory(configurationOverrides);
    }

    private static AuthenticatedWebApplicationFactory CreateFactoryWithSetupSecretWithoutClaimsTransformation()
    {
        return new PassthroughClaimsTransformationFactory();
    }

    private sealed class ConfigurableAuthenticatedWebApplicationFactory : OnboardingWebApplicationFactory
    {
        private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;

        public ConfigurableAuthenticatedWebApplicationFactory(IReadOnlyDictionary<string, string?> configurationOverrides)
        {
            _configurationOverrides = configurationOverrides;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(_configurationOverrides);
            });
        }
    }

    private static HttpRequestMessage CreateInstanceAdminRequest(HttpMethod method, string url, Guid userId, object? body, bool includeSetupSecret)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Instance Admin",
                ("iss", OnboardingWebApplicationFactory.Issuer),
                ("idp", "keycloak")));

        if (includeSetupSecret)
        {
            request.Headers.Add("X-Setup-Secret", SetupSecret);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static HttpRequestMessage CreateCustomAuthRequest(
        HttpMethod method,
        string url,
        object? body,
        bool includeSetupSecret,
        params TestAuthHandler.TestClaimDto[] claims)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, EncodeClaims(claims));

        if (includeSetupSecret)
        {
            request.Headers.Add("X-Setup-Secret", SetupSecret);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static string EncodeClaims(params TestAuthHandler.TestClaimDto[] claims)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)));
    }

    private static async Task<string?> ReadGenerationAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/journey");
        request.Headers.Add("X-Setup-Secret", SetupSecret);
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var journey = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return journey.RootElement.GetProperty("generation").GetString();
    }

    private static CompleteInstanceOnboardingRequest CreateValidOnboardingRequest()
    {
        return new CompleteInstanceOnboardingRequest
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Integration Test Instance" },
            DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
            {
                PublicName = "Integration Test Operator",
                LegalName = "Integration Test Operator",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE",
                PublicContactEmail = "operator@integration.test",
                LegalNoticeUrl = "https://integration.test/legal",
                PrivacyUrl = "https://integration.test/privacy"
            },
            InstanceName = "Integration Test Instance"
        };
    }

    private static async Task AssertDeploymentKeycloakConfiguration(
        AuthProviderConfigurationDto? configuration,
        string authority,
        string clientId)
    {
        await Assert.That(configuration).IsNotNull();
        await Assert.That(configuration!.PrimaryProviderId)
            .IsEqualTo((int)AuthenticationProviderKind.Keycloak);
        await Assert.That(configuration.KeycloakDetectedFromEnvironment).IsTrue();
        await Assert.That(configuration.KeycloakAuthority).IsEqualTo(authority);
        await Assert.That(configuration.KeycloakClientId).IsEqualTo(clientId);
        await Assert.That(configuration.KeycloakClientSecret).IsEqualTo(string.Empty);
    }

    private static AuthProviderConfigurationDto CreateGoogleOnlyAuthProviderConfiguration()
    {
        return new AuthProviderConfigurationDto
        {
            KeycloakAuthority = string.Empty,
            KeycloakClientId = string.Empty,
            KeycloakClientSecret = string.Empty,
            AtprotoLoginEnabled = false,
            AtprotoPublicUrl = string.Empty,
            GoogleSsoEnabled = true,
            GoogleClientId = "google-client-id",
            GoogleClientSecret = "google-client-secret",
            LockAtprotoLoginEnabled = false,
            LockGoogleSsoEnabled = false
        };
    }

    private static PatchAuthProviderConfigurationDto CreateGoogleOnlyAuthProviderPatch()
    {
        var configuration = CreateGoogleOnlyAuthProviderConfiguration();
        return new PatchAuthProviderConfigurationDto
        {
            Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new AuthProviderConfigurationWriteDto
            {
                PrimaryProviderId = configuration.PrimaryProviderId,
                KeycloakAuthority = configuration.KeycloakAuthority,
                KeycloakClientId = configuration.KeycloakClientId,
                AtprotoLoginEnabled = configuration.AtprotoLoginEnabled,
                AtprotoPublicUrl = configuration.AtprotoPublicUrl,
                GoogleSsoEnabled = configuration.GoogleSsoEnabled,
                GoogleClientId = configuration.GoogleClientId,
                GoogleClientSecret = configuration.GoogleClientSecret,
                LockAtprotoLoginEnabled = configuration.LockAtprotoLoginEnabled,
                LockGoogleSsoEnabled = configuration.LockGoogleSsoEnabled
            })
        };
    }

    private static AuthorizationProviderConfigurationDto CreateLocalAuthorizationProviderConfiguration()
    {
        return new AuthorizationProviderConfigurationDto
        {
            Provider = "local",
            CerbosGrpcEndpoint = string.Empty,
            CerbosDetectedFromEnvironment = false,
            CerbosEndpointVerified = false
        };
    }

    private static PatchAuthorizationProviderConfigurationDto CreateLocalAuthorizationProviderPatch()
    {
        var configuration = CreateLocalAuthorizationProviderConfiguration();
        return new PatchAuthorizationProviderConfigurationDto
        {
            Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(new AuthorizationProviderConfigurationWriteDto
            {
                Provider = configuration.Provider,
                CerbosGrpcEndpoint = configuration.CerbosGrpcEndpoint,
                CerbosAdminEndpoint = configuration.CerbosAdminEndpoint
            })
        };
    }

    private sealed class AuthProviderConfiguredResponse
    {
        public bool Configured { get; set; }

        public bool AuthorizationProviderManagedByDeployment { get; set; }

        public string? AuthorizationProviderBootstrapStatus { get; set; }
    }

    private static bool HasProblemDetailsResponse(System.Reflection.MethodInfo action, int statusCode)
    {
        return action.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: false)
            .Cast<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == statusCode && attribute.Type == typeof(ProblemDetails));
    }

    private sealed class PassthroughClaimsTransformationFactory : OnboardingWebApplicationFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClaimsTransformation>();
                services.AddSingleton<IClaimsTransformation, PassthroughClaimsTransformation>();
            });
        }
    }

    private sealed class PassthroughClaimsTransformation : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            return Task.FromResult(principal);
        }
    }
}
