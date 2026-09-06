// ABOUTME: Verifies public Local admission policy through production HTTP, Identity, and account synchronization.
// ABOUTME: Guards closed registration and email-verification enforcement without substituting authentication services.

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class LocalAdmissionPolicyHttpTests
{
    [Test]
    public async Task PublicRegistrationDoesNotCreateIdentityOrDomainCredentials()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        await using ExploreDbContext before = factory.CreateDatabase();
        int identitiesBefore = await before.LocalIdentityUsers.CountAsync();
        int usersBefore = await before.Users.CountAsync();
        string email = $"signup-{Guid.CreateVersion7():N}@example.test";

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/local/register", new
        {
            Email = email,
            Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            FirstName = "Public",
            LastName = "Applicant"
        });

        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.LocalIdentityUsers.CountAsync()).IsEqualTo(identitiesBefore);
        await Assert.That(await stored.Users.CountAsync()).IsEqualTo(usersBefore);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task EnabledInstanceEmailRejectsUnverifiedLocalLoginEvenWithoutSmtpHost()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: false);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/local/login", login);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert.That(body.RootElement.GetProperty("code").GetString())
            .IsEqualTo("email_verification_required");
        await Assert.That(body.RootElement.TryGetProperty("token", out _)).IsFalse();
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That((await stored.LocalIdentityUsers.SingleAsync(user => user.Email == login.Email))
            .EmailConfirmed).IsFalse();
        await Assert.That(await stored.Users.AnyAsync(user => user.Pii.Email == login.Email)).IsFalse();
    }

    [Test]
    public async Task VerifiedLocalLoginIssuesNativeValidTokenAndSynchronizesDomainAccount()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: true);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/local/login", login);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        await Assert.That(body.RootElement.GetProperty("emailVerified").GetBoolean()).IsTrue();
        string token = body.RootElement.GetProperty("token").GetString()!;
        JwtBearerOptions bearer = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(ApiAuthenticationSchemeNames.LocalIdentity);
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = tokenHandler.ValidateToken(token, bearer.TokenValidationParameters, out _);
        await Assert.That(principal.FindFirst("email_verified")?.Value).IsEqualTo("true");

        await using ExploreDbContext stored = factory.CreateDatabase();
        var identity = await stored.LocalIdentityUsers.SingleAsync(user => user.Email == login.Email);
        var linked = await stored.UserExternalLogins.Include(binding => binding.User).SingleAsync(
            binding => binding.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
                && binding.ProviderKey == identity.Id.ToString());
        await Assert.That(linked.User.Email).IsEqualTo(login.Email);
        await Assert.That(linked.User.EmailVerified).IsEqualTo(true);
    }

    [Test]
    [Arguments("false")]
    [Arguments(null)]
    public async Task DisabledOrAbsentInstanceIntentAllowsLoginWithoutConfirmingEmail(string? intent)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: false);
        await SetInstanceIntentAsync(factory, intent);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/local/login", login);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert.That(body.RootElement.GetProperty("emailVerified").GetBoolean()).IsFalse();
        await AssertUnconfirmedAsync(factory, login.Email);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task TenantOverrideCannotReplaceInstanceAdmissionPolicy(bool instanceEnabled)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: false);
        await SetInstanceIntentAsync(factory, instanceEnabled ? "true" : "false");
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            seed.Set<TenantSetting>().Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = await seed.Tenants.SingleAsync(tenant => tenant.Id == PlatformDefaults.DefaultTenantId),
                SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
                Value = instanceEnabled ? "false" : "true",
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/local/login", login);

        await Assert.That(response.StatusCode).IsEqualTo(
            instanceEnabled ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        await AssertUnconfirmedAsync(factory, login.Email);
    }

    [Test]
    [Arguments("\"true\"")]
    [Arguments("invalid-json")]
    public async Task MalformedInstanceIntentFailsClosedWithoutIssuingSession(string intent)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: false);
        await SetInstanceIntentAsync(factory, intent);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/local/login", login);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo("authentication_failed");
        await Assert.That(body.RootElement.TryGetProperty("token", out _)).IsFalse();
        await AssertUnconfirmedAsync(factory, login.Email);
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.Users.AnyAsync(user => user.Pii.Email == login.Email)).IsFalse();
    }

    [Test]
    public async Task CommittedIntentChangesTakeEffectOnNextLoginWithoutPromotingVerification()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: false);
        await SetInstanceIntentAsync(factory, "false");
        using HttpResponseMessage before = await client.PostAsJsonAsync("/api/auth/local/login", login);
        await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await SetInstanceIntentAsync(factory, "true");
        using HttpResponseMessage enabled = await client.PostAsJsonAsync("/api/auth/local/login", login);
        await Assert.That(enabled.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        await SetInstanceIntentAsync(factory, "false");
        using HttpResponseMessage disabled = await client.PostAsJsonAsync("/api/auth/local/login", login);
        await Assert.That(disabled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertUnconfirmedAsync(factory, login.Email);
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That((await stored.Users.SingleAsync(user => user.Pii.Email == login.Email)).EmailVerified)
            .IsEqualTo(false);
    }

    [Test]
    public async Task ReusedIdempotencyKeyCannotReplaySessionAfterVerificationBecomesRequired()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: false);
        await SetInstanceIntentAsync(factory, "false");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));

        using HttpResponseMessage before = await client.PostAsJsonAsync("/api/auth/local/login", login);
        await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await SetInstanceIntentAsync(factory, "true");

        using HttpResponseMessage after = await client.PostAsJsonAsync("/api/auth/local/login", login);

        await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using JsonDocument body = await JsonDocument.ParseAsync(await after.Content.ReadAsStreamAsync());
        await Assert.That(body.RootElement.GetProperty("code").GetString())
            .IsEqualTo("email_verification_required");
        await Assert.That(body.RootElement.TryGetProperty("token", out _)).IsFalse();
        await AssertUnconfirmedAsync(factory, login.Email);
    }

    [Test]
    public async Task LocalSessionResponseIsPrivateAndNeverPersistedForIdempotentReplay()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: true);
        string key = Guid.CreateVersion7().ToString("N");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/local/login", login);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.Set<IdempotencyRecord>().AnyAsync(record => record.Key == key))
            .IsFalse();
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
    }

    private static async Task SetInstanceIntentAsync(LocalAdmissionWebApplicationFactory factory, string? value)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        if (value is null)
        {
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            SystemSetting existing = await database.SystemSettings.SingleAsync(
                setting => setting.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
            database.SystemSettings.Remove(existing);
            await database.SaveChangesAsync();
            return;
        }

        var repository = scope.ServiceProvider.GetRequiredService<ISystemSettingRepository>();
        await repository.UpsertAsync(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
            Value = value,
            ValueType = SettingValueType.Boolean,
            Category = "Email",
            CreatedAt = DateTime.UtcNow
        });
    }

    private static async Task AssertUnconfirmedAsync(LocalAdmissionWebApplicationFactory factory, string email)
    {
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That((await stored.LocalIdentityUsers.SingleAsync(user => user.Email == email)).EmailConfirmed)
            .IsFalse();
    }

    private static HttpClient CreateClient(LocalAdmissionWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
}
