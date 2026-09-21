using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Onboarding;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class CompletionReadinessHttpTests
{
    [Test]
    [Arguments(AuthenticationProviderKind.Local, "failed")]
    [Arguments(AuthenticationProviderKind.Local, "pending")]
    [Arguments(AuthenticationProviderKind.Local, "action-required")]
    [Arguments(AuthenticationProviderKind.Local, "ready")]
    [Arguments(AuthenticationProviderKind.Keycloak, "failed")]
    [Arguments(AuthenticationProviderKind.Keycloak, "pending")]
    [Arguments(AuthenticationProviderKind.Keycloak, "action-required")]
    [Arguments(AuthenticationProviderKind.Keycloak, "ready")]
    public async Task ExactGenerationCannotBypassBlockingAuthorizationReadiness(
        AuthenticationProviderKind provider, string readiness)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: provider, incompleteSetup: true);
        await using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Keycloak:ClientId"] = "islamu-event-blazor" }));
            builder.ConfigureTestServices(services => services.PostConfigure<AuthorizationProviderDeploymentOptions>(
                options => options.Provider = readiness == "action-required" ? null : "cerbos"));
        });
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var bootstrapState = configured.Services.GetRequiredService<AuthorizationProviderBootstrapState>();
        switch (readiness)
        {
            case "ready": bootstrapState.MarkReady("cerbos", true, true, "Ready"); break;
            case "failed": bootstrapState.MarkFailed("cerbos", false, "Unavailable"); break;
            case "pending": bootstrapState.MarkPending("cerbos"); break;
        }
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        if (provider == AuthenticationProviderKind.Keycloak)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                factory.CreateExternalProviderToken(Guid.CreateVersion7(), "operator@example.test", true));
        using var profileResponse = await client.PatchAsJsonAsync("/api/instanceonboarding/profile",
            new SelfHostOnboardingProfileDto { SiteName = "Readiness invariant", CanonicalUrl = "https://example.test" }, token);
        await Assert.That(profileResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var journeyResponse = await client.GetAsync("/api/instanceonboarding/journey", token);
        await Assert.That(journeyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var journey = JsonDocument.Parse(await journeyResponse.Content.ReadAsStringAsync(token));
        await Assert.That(journey.RootElement.GetProperty("state").GetString()).IsEqualTo("Available");
        var checks = journey.RootElement.GetProperty("preflight").GetProperty("blockingChecks").EnumerateArray().ToArray();
        await Assert.That(checks.Where(check => check.GetProperty("code").GetString() != "authorization_config")
            .All(check => check.GetProperty("status").GetString() == "Pass")).IsTrue()
            .Because(string.Join(",", checks.Where(check => check.GetProperty("status").GetString() != "Pass")
                .Select(check => check.GetProperty("code").GetString())));
        await Assert.That(checks.Single(check => check.GetProperty("code").GetString() == "authorization_config")
            .GetProperty("status").GetString()).IsEqualTo(readiness == "ready" ? "Pass" : "Fail");
        var settings = new CompleteInstanceOnboardingRequest
        {
            ExpectedJourneyGeneration = journey.RootElement.GetProperty("generation").GetString(),
            SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Readiness invariant", CanonicalUrl = "https://example.test" }
        };
        using var response = provider == AuthenticationProviderKind.Local
            ? await client.PostAsJsonAsync("/api/instanceonboarding/complete-local", new CompleteLocalInstanceOnboardingRequestDto
            {
                OperationId = Guid.CreateVersion7(), Username = $"operator-{Guid.CreateVersion7():N}",
                TemporaryPassword = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}", Settings = settings
            }, token)
            : await client.PostAsJsonAsync("/api/instanceonboarding/complete", settings, token);
        await using var database = factory.CreateDatabase();
        if (readiness == "ready")
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await database.InstanceBootstrapStates.SingleAsync(token)).Status).IsEqualTo(InstanceBootstrapStatus.Completed);
            await Assert.That(await database.PlatformUserRoles.CountAsync(token)).IsEqualTo(1);
            return;
        }
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(409);
        await Assert.That(await database.InstanceBootstrapStates.AnyAsync(state => state.Status == InstanceBootstrapStatus.Completed, token)).IsFalse();
        await Assert.That(await database.PlatformUserRoles.AnyAsync(token)).IsFalse();
        await Assert.That(await database.LocalIdentityUsers.AnyAsync(token)).IsFalse();
        await Assert.That(await database.LocalIdentityLifecycleOperations.AnyAsync(token)).IsFalse();
        using var stillAuthorized = await client.GetAsync("/api/instanceonboarding/journey", token);
        await Assert.That(stillAuthorized.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var status = JsonDocument.Parse(await stillAuthorized.Content.ReadAsStringAsync(token));
        await Assert.That(status.RootElement.GetProperty("bootstrap").GetProperty("isSetupModeActive").GetBoolean()).IsTrue();
    }
}
