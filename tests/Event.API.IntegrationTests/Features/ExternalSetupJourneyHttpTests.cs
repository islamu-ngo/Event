extern alias bff;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Explore.Application.DTOs.Onboarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;
using BffServices = bff::Explore.Blazor.Services;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class ExternalSetupJourneyHttpTests
{
    [Test]
    [Arguments("active", HttpStatusCode.OK)]
    [Arguments("forged", HttpStatusCode.Forbidden)]
    [Arguments("expired", HttpStatusCode.Forbidden)]
    [Arguments("missing", HttpStatusCode.Forbidden)]
    [Arguments("invalid-bearer", HttpStatusCode.Unauthorized)]
    public async Task ExternalSignInRetainsOnlyProtectedSetupJourneyAuthority(string authority, HttpStatusCode expected)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak, incompleteSetup: true);
        await using (var database = factory.CreateDatabase())
            await database.Tenants.ExecuteDeleteAsync(cancellationToken);
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:ClientId"] = "islamu-event-blazor",
                ["Authorization:Provider"] = "local"
            })));
        var protection = new EphemeralDataProtectionProvider();
        var protector = new BffServices.SetupSecretCookieProtector(protection);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = authority == "missing" ? string.Empty : "setup-secret=" + (authority switch
        {
            "active" or "invalid-bearer" => protector.Protect(factory.SetupSecret),
            "expired" => protection.CreateProtector("Explore.Blazor.SetupSecretCookie.v1")
                .ToTimeLimitedDataProtector().Protect(factory.SetupSecret, DateTimeOffset.UtcNow.AddMinutes(-1)),
            _ => Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        });
        var accessor = new HttpContextAccessor { HttpContext = context };
        var resolver = new BffServices.SetupSecretResolver(accessor,
            new BffServices.SetupSecretSessionService(), protector,
            Options.Create(new BffServices.SetupSecretResolverOptions()),
            configured.Services.GetRequiredService<IHostEnvironment>());
        using var handler = new BffServices.SetupSecretForwardingHandler(resolver)
        {
            InnerHandler = configured.Server.CreateHandler()
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            authority == "invalid-bearer" ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
                : factory.CreateExternalProviderToken(Guid.CreateVersion7(), "operator@example.test", true));
        // Browser-supplied setup authority must be stripped even after provider sign-in.
        client.DefaultRequestHeaders.Add("X-Setup-Secret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        using var response = await client.GetAsync("/api/instanceonboarding/journey", cancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (expected == HttpStatusCode.OK)
        {
            await Assert.That(body.RootElement.GetProperty("bootstrap").GetProperty("isSetupModeActive").GetBoolean()).IsTrue();
            await Assert.That(body.RootElement.GetProperty("bootstrap").GetProperty("isAuthenticated").GetBoolean()).IsTrue();
            await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("save-profile", out _)).IsTrue();
            using var profile = await client.PatchAsJsonAsync("/api/instanceonboarding/profile",
                new SelfHostOnboardingProfileDto { SiteName = "External setup", CanonicalUrl = "https://example.test" }, cancellationToken);
            await Assert.That(profile.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var readyResponse = await client.GetAsync("/api/instanceonboarding/journey", cancellationToken);
            await Assert.That(readyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var ready = JsonDocument.Parse(await readyResponse.Content.ReadAsStringAsync(cancellationToken));
            await Assert.That(ready.RootElement.GetProperty("preflight").GetProperty("isReadyToLaunch").GetBoolean()).IsTrue()
                .Because(string.Join(",", ready.RootElement.GetProperty("preflight").GetProperty("blockingChecks").EnumerateArray()
                    .Where(check => check.GetProperty("status").GetString() != "Pass").Select(check => check.GetProperty("code").GetString())));
            await Assert.That(ready.RootElement.GetProperty("_links").TryGetProperty("complete", out _)).IsTrue();
            using var completed = await client.PostAsJsonAsync("/api/instanceonboarding/complete",
                new CompleteInstanceOnboardingRequest
                {
                    SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "External setup", CanonicalUrl = "https://example.test" },
                    ExpectedJourneyGeneration = ready.RootElement.GetProperty("generation").GetString()
                }, cancellationToken);
            await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            // The old protected setup cookie remains: it must not shadow the new administrator's bearer.
            using var administrator = await client.GetAsync("/api/instanceonboarding/journey", cancellationToken);
            await Assert.That(administrator.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var final = JsonDocument.Parse(await administrator.Content.ReadAsStringAsync(cancellationToken));
            await Assert.That(final.RootElement.GetProperty("bootstrap").GetProperty("isCurrentUserInstanceAdmin").GetBoolean()).IsTrue();
            await Assert.That(final.RootElement.GetProperty("_links").TryGetProperty("complete", out _)).IsFalse();
            using var current = await client.GetAsync("/api/user", cancellationToken);
            await Assert.That(current.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var adminAuthority = await client.GetAsync("/api/user/admin-authority", cancellationToken);
            await Assert.That(adminAuthority.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var publicRead = await client.GetAsync("/api/PublicExperience/settings", cancellationToken);
            await Assert.That(publicRead.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using var locked = await client.PatchAsJsonAsync("/api/instanceonboarding/profile",
                new SelfHostOnboardingProfileDto { SiteName = "Rejected replay" }, cancellationToken);
            await Assert.That(locked.StatusCode).IsEqualTo(HttpStatusCode.Gone);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                factory.CreateExternalProviderToken(Guid.CreateVersion7(), "other@example.test", true));
            using var otherAccount = await client.GetAsync("/api/instanceonboarding/journey", cancellationToken);
            await Assert.That(otherAccount.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await using var database = factory.CreateDatabase();
            await Assert.That(await database.PlatformUserRoles.CountAsync(cancellationToken)).IsEqualTo(1);
            await Assert.That((await database.Tenants.SingleAsync(cancellationToken)).TenantStatusId)
                .IsEqualTo((int)TenantStatusEnum.Provisioning);
        }
        else
        {
            await Assert.That(body.RootElement.TryGetProperty("profile", out _)).IsFalse();
        }
        if (authority is "missing" or "forged" or "expired")
        {
            using var rejected = await client.PatchAsJsonAsync("/api/instanceonboarding/profile",
                new SelfHostOnboardingProfileDto { SiteName = "Rejected", CanonicalUrl = "https://untrusted.example.test" }, cancellationToken);
            await Assert.That(rejected.IsSuccessStatusCode).IsFalse();
            using var scope = configured.Services.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Persistence.ISystemSettingRepository>();
            await Assert.That(await settings.GetByKey(Explore.Domain.Constants.GovernanceSettingKeys.Domains.PublicBaseUrl, cancellationToken)).IsNull();
        }
        accessor.HttpContext = null;
    }
}
