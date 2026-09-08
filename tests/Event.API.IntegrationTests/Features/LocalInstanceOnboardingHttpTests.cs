
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.TenantSettings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class LocalInstanceOnboardingHttpTests
{
    private const string CompletePath = "/api/instanceonboarding/complete-local";
    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    public async Task OptionalEmailSetupRequiresReplacementBeforeFreshProtectedLogin(IdentityDatabaseTopology topology, bool supplyEmail)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            identityTopology: topology, incompleteSetup: true);
        using HttpClient client = CreateClient(factory);
        var request = Request() with { Email = supplyEmail ? $"local-{Guid.CreateVersion7():N}@example.test" : null };
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        client.DefaultRequestHeaders.Add("Idempotency-Key", request.OperationId.ToString("D"));
        using HttpResponseMessage response = await client.PostAsJsonAsync(CompletePath, request, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(CancellationToken));
        await Assert.That(body.RootElement.TryGetProperty("token", out _)).IsFalse();
        await Assert.That(body.RootElement.TryGetProperty("replacementChallenge", out _)).IsFalse();
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await using (ExploreDbContext database = factory.CreateDatabase())
        {
            var bootstrap = await database.InstanceBootstrapStates.SingleAsync(CancellationToken);
            await Assert.That(bootstrap.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
            await Assert.That(await database.PlatformUserRoles.AnyAsync(role => role.UserId == bootstrap.CompletedByUserId, CancellationToken)).IsTrue();
            await Assert.That(await database.Set<IdempotencyRecord>().AnyAsync(record => record.Key == request.OperationId.ToString("D"), CancellationToken)).IsFalse();
        }
        using HttpResponseMessage replay = await client.PostAsJsonAsync(CompletePath, request, CancellationToken);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Gone);
        client.DefaultRequestHeaders.Remove("X-Setup-Secret");
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        using JsonDocument completedStatus = await StatusAsync(client);
        await Assert.That(completedStatus.RootElement.GetProperty("_links").TryGetProperty("complete-local", out _)).IsFalse();
        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = request.Username, password = request.TemporaryPassword }, CancellationToken);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument challengeBody = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(CancellationToken));
        await Assert.That(challengeBody.RootElement.TryGetProperty("token", out _)).IsFalse();
        string challenge = challengeBody.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", challenge);
        using HttpResponseMessage denied = await client.GetAsync("/api/user", CancellationToken);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        string replacement = NewPassword();
        using HttpResponseMessage replaced = await client.PostAsJsonAsync("/api/auth/local/credential-replacement",
            new { newPassword = replacement }, CancellationToken);
        await Assert.That(replaced.IsSuccessStatusCode).IsTrue();
        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage fresh = await client.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = request.Username, password = replacement }, CancellationToken);
        await Assert.That(fresh.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument freshBody = await JsonDocument.ParseAsync(await fresh.Content.ReadAsStreamAsync(CancellationToken));
        await Assert.That(freshBody.RootElement.TryGetProperty("email", out var authenticatedEmail)).IsEqualTo(supplyEmail);
        if (supplyEmail) await Assert.That(authenticatedEmail.GetString()).IsEqualTo(request.Email);
        string token = freshBody.RootElement.GetProperty("token").GetString()!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        await Assert.That(jwt.Claims.SingleOrDefault(claim => claim.Type == "email")?.Value).IsEqualTo(request.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage current = await client.GetAsync("/api/user", CancellationToken);
        await Assert.That(current.StatusCode).IsEqualTo(HttpStatusCode.OK);
        if (supplyEmail)
        {
            using HttpResponseMessage byEmail = await client.PostAsJsonAsync("/api/auth/local/login",
                new { identifier = request.Email, password = replacement }, CancellationToken);
            await Assert.That(byEmail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        await using (ExploreDbContext database = factory.CreateDatabase())
        {
            UserExternalLogin binding = await database.UserExternalLogins.SingleAsync(
                row => row.ProviderKey == jwt.Subject, CancellationToken);
            database.UserExternalLogins.Remove(binding);
            await database.SaveChangesAsync(CancellationToken);
        }
        using HttpResponseMessage detachedBearer = await client.GetAsync("/api/user", CancellationToken);
        await Assert.That(detachedBearer.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage detachedLogin = await client.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = request.Username, password = replacement }, CancellationToken);
        await Assert.That(detachedLogin.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrWrongSetupAuthorityCannotCreateCredentials(bool wrongSecret)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        using HttpClient client = CreateClient(factory);
        if (wrongSecret) client.DefaultRequestHeaders.Add("X-Setup-Secret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        using HttpResponseMessage response = await client.PostAsJsonAsync(CompletePath, Request(), CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await using ExploreDbContext database = factory.CreateDatabase();
        await Assert.That(await database.LocalIdentityUsers.AnyAsync(CancellationToken)).IsFalse();
    }

    [Test]
    public async Task ExternalProviderDoesNotAdvertiseOrAcceptLocalSetup()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak, incompleteSetup: true);
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.CreateExternalProviderToken(Guid.CreateVersion7(), "operator@example.test", true));
        using HttpResponseMessage bearerOnly = await client.PostAsJsonAsync(CompletePath, Request(), CancellationToken);
        await Assert.That(bearerOnly.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        using HttpResponseMessage response = await client.PostAsJsonAsync(CompletePath, Request(), CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await using ExploreDbContext database = factory.CreateDatabase();
        await Assert.That(await database.LocalIdentityUsers.AnyAsync(CancellationToken)).IsFalse();
        using JsonDocument status = await StatusAsync(client);
        await Assert.That(status.RootElement.GetProperty("_links").TryGetProperty("complete-local", out _)).IsFalse();
    }

    [Test]
    public async Task LocalCompletionAffordanceRequiresNativeSetupAuthority()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        using HttpClient client = CreateClient(factory);
        using JsonDocument anonymous = await StatusAsync(client);
        await Assert.That(anonymous.RootElement.GetProperty("_links").TryGetProperty("complete-local", out _)).IsFalse();
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        using JsonDocument authorized = await StatusAsync(client);
        await Assert.That(authorized.RootElement.GetProperty("provider").GetString()).IsEqualTo("Local");
        await Assert.That(authorized.RootElement.GetProperty("_links").GetProperty("complete-local").GetProperty("method").GetString()).IsEqualTo("POST");
        await Assert.That(authorized.RootElement.GetProperty("_links").TryGetProperty("complete", out _)).IsFalse();
        await Assert.That(authorized.RootElement.GetProperty("_links").TryGetProperty("save-profile", out _)).IsFalse();
    }

    [Test]
    public async Task NativeSetupRateLimitBoundsInvalidEnrollmentAttempts()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true, enableRateLimiting: true);
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        var request = Request() with { OperationId = Guid.Empty };
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(CompletePath, request, CancellationToken);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using HttpResponseMessage limited = await client.PostAsJsonAsync(CompletePath, request, CancellationToken);
        await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    private static async Task<JsonDocument> StatusAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/instanceonboarding/status", CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(CancellationToken));
    }

    [Test]
    public async Task RestartRecoversReservedOperationOnlyThroughNativeSetupStatus()
    {
        var fault = new CredentialWriteFault();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            incompleteSetup: true, persistenceInterceptor: fault);
        var original = Request();
        using (HttpClient firstBrowser = CreateClient(factory))
        {
            firstBrowser.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
            using JsonDocument initial = await StatusAsync(firstBrowser);
            await Assert.That(initial.RootElement.TryGetProperty("pendingOperationId", out _)).IsFalse();
            fault.Armed = true;
            using HttpResponseMessage interrupted = await firstBrowser.PostAsJsonAsync(CompletePath, original, CancellationToken);
            await Assert.That(interrupted.IsSuccessStatusCode).IsFalse();
            await Assert.That(fault.Observed).IsTrue();
        }
        await using var restarted = factory.WithWebHostBuilder(_ => { });
        using HttpClient browser = restarted.CreateClient(new WebApplicationFactoryClientOptions
            { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using JsonDocument anonymous = await StatusAsync(browser);
        await Assert.That(anonymous.RootElement.TryGetProperty("pendingOperationId", out _)).IsFalse();
        browser.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        using JsonDocument status = await StatusAsync(browser);
        Guid recovered = status.RootElement.GetProperty("pendingOperationId").GetGuid();
        await Assert.That(recovered).IsEqualTo(original.OperationId);
        using HttpResponseMessage wrongOperation = await browser.PostAsJsonAsync(CompletePath,
            original with { OperationId = Guid.CreateVersion7() }, CancellationToken);
        await Assert.That(wrongOperation.IsSuccessStatusCode).IsFalse();
        using HttpResponseMessage completed = await browser.PostAsJsonAsync(CompletePath,
            original with { OperationId = recovered }, CancellationToken);
        await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument result = await JsonDocument.ParseAsync(await completed.Content.ReadAsStreamAsync(CancellationToken));
        await Assert.That(result.RootElement.TryGetProperty("token", out _)).IsFalse();
        await Assert.That(result.RootElement.TryGetProperty("replacementChallenge", out _)).IsFalse();
        browser.DefaultRequestHeaders.Remove("X-Setup-Secret");
        using JsonDocument finalStatus = await StatusAsync(browser);
        await Assert.That(finalStatus.RootElement.TryGetProperty("pendingOperationId", out _)).IsFalse();
        using HttpResponseMessage login = await browser.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = original.Username, password = original.TemporaryPassword }, CancellationToken);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument challenge = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(CancellationToken));
        await Assert.That(challenge.RootElement.TryGetProperty("token", out _)).IsFalse();
        await Assert.That(challenge.RootElement.TryGetProperty("replacementChallenge", out _)).IsTrue();
    }

    private sealed class InterruptedCredentialWrite : Exception;
    private sealed class CredentialWriteFault : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Observed { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.CommandSource == CommandSource.SaveChanges
                && eventData.Context!.ChangeTracker.Entries<LocalIdentityUser>().Any(entry => entry.State == EntityState.Added))
            {
                Armed = false;
                Observed = true;
                throw new InterruptedCredentialWrite();
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task MissingDirectoryOperatorCannotBeInferredFromCredentialEmail()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        var valid = Request();
        var request = valid with { Email = $"private-{Guid.CreateVersion7():N}@example.test",
            Settings = valid.Settings with { DirectoryOperatorIdentity = null } };
        using HttpResponseMessage response = await client.PostAsJsonAsync(CompletePath, request, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync(CancellationToken);
        await Assert.That(body.Contains(request.TemporaryPassword, StringComparison.Ordinal)
            || body.Contains(request.Email, StringComparison.Ordinal)
            || body.Contains(request.Username, StringComparison.Ordinal)).IsFalse();
        await using ExploreDbContext database = factory.CreateDatabase();
        await Assert.That(await database.LocalIdentityUsers.AnyAsync(CancellationToken)).IsFalse();
    }

    private static CompleteLocalInstanceOnboardingRequestDto Request() => new()
    {
        OperationId = Guid.CreateVersion7(), Username = $"operator-{Guid.CreateVersion7():N}", TemporaryPassword = NewPassword(),
        Settings = new CompleteInstanceOnboardingRequest
        {
            SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Native Local Instance" },
            DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
            {
                PublicName = "Directory Operator", LegalName = "Directory Operator", OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE", PublicContactEmail = "operator@example.test",
                LegalNoticeUrl = "https://example.test/legal", PrivacyUrl = "https://example.test/privacy"
            }
        }
    };

    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
    private static HttpClient CreateClient(LocalAdmissionWebApplicationFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
}
