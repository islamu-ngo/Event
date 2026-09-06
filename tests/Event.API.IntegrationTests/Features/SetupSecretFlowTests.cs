// ABOUTME: Integration tests for the setup-secret validation flow.
// ABOUTME: Covers correct/wrong secret validation, tenant-exempt path behavior, and 410 after completion.

using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

public class SetupSecretFlowTests
{
    private const string BaseUrl = "/api/instanceonboarding";
    private static string SetupSecret => OnboardingWebApplicationFactory.SetupSecret;

    [Test]
    public async Task ValidateSecret_WithCorrectSecret_ShouldReturn200WithValidTrue()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{BaseUrl}/validate-secret", new { secret = SetupSecret });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ValidateSecretResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Valid).IsTrue();
    }

    [Test]
    public async Task ValidateSecret_WithWrongSecret_ShouldReturn200WithValidFalse()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{BaseUrl}/validate-secret", new { secret = "wrong-secret" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ValidateSecretResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Valid).IsFalse();
    }

    [Test]
    public async Task ValidateSecret_WithWrongSecret_ShouldEmitRejectedAuditEvent()
    {
        var auditLogger = new CapturingBootstrapAuditLogger();
        using var factory = CreateFactoryWithSetupSecret(auditLogger);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{BaseUrl}/validate-secret", new { secret = "wrong-secret" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(auditLogger.Events.Any(auditEvent =>
            auditEvent.EventType == InstanceBootstrapAuditEventType.SetupSecretRejected
            && auditEvent.Operation == "setup_secret_validate"
            && auditEvent.Outcome == "rejected"
            && auditEvent.FailureCode == "invalid_setup_secret"
            && !auditEvent.ToString().Contains("wrong-secret", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ValidateSecret_WithoutTenantHeader_ShouldSucceed()
    {
        // The validate-secret endpoint is tenant-exempt so it works before any tenant exists.
        // No tenant header is set — the middleware should skip tenant resolution for this path.
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{BaseUrl}/validate-secret", new { secret = SetupSecret });

        // Should NOT return 404 (tenant not found) — path is exempt
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task ValidateSecret_AfterBootstrapComplete_ShouldReturn410Gone()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        // Complete onboarding to end setup mode
        var completePayload = CreateValidSettings();
        using var completeRequest = CreateInstanceAdminRequest(
            HttpMethod.Post, $"{BaseUrl}/complete", userId, completePayload, includeSetupSecret: true);
        var completeResponse = await client.SendAsync(completeRequest);
        await Assert.That(completeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Now validate-secret should return 410 Gone
        var response = await client.PostAsJsonAsync($"{BaseUrl}/validate-secret", new { secret = SetupSecret });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }

    [Test]
    public async Task GetOnboardingStatus_WithoutTenantHeader_ShouldSucceed()
    {
        // The status endpoint is also under /api/InstanceOnboarding — tenant-exempt
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{BaseUrl}/status");

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Complete_WithValidSecretHeader_ShouldSucceed()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        var completePayload = CreateValidSettings();
        using var completeRequest = CreateInstanceAdminRequest(
            HttpMethod.Post, $"{BaseUrl}/complete", userId, completePayload, includeSetupSecret: true);
        var completeResponse = await client.SendAsync(completeRequest);

        await Assert.That(completeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await completeResponse.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Complete_WithInvalidSecretHeader_ShouldReturnForbidden()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        var completePayload = CreateValidSettings();
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/complete");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateInstanceAdminHeaderValue(userId));
        request.Headers.Add("X-Setup-Secret", "wrong-secret");
        request.Content = JsonContent.Create(completePayload);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Complete_WithValidSecretButNoAuthentication_ShouldReturnUnauthorized()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/complete");
        request.Headers.Add("X-Setup-Secret", SetupSecret);
        request.Content = JsonContent.Create(CreateValidSettings());

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Complete_WithAuthenticationButMissingSetupSecret_ShouldReturnForbidden()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        using var request = CreateInstanceAdminRequest(
            HttpMethod.Post, $"{BaseUrl}/complete", userId, CreateValidSettings(), includeSetupSecret: false);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SetupProtectedInternalEndpoint_AfterBootstrapComplete_ShouldReturn410Gone()
    {
        using var factory = CreateFactoryWithSetupSecret();
        using var client = factory.CreateClient();

        var userId = Guid.CreateVersion7();
        await EnsureUserExistsAsync(factory, userId);

        using var completeRequest = CreateInstanceAdminRequest(
            HttpMethod.Post, $"{BaseUrl}/complete", userId, CreateValidSettings(), includeSetupSecret: true);
        var completeResponse = await client.SendAsync(completeRequest);
        await Assert.That(completeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var internalRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/auth-provider-configuration/internal");
        internalRequest.Headers.Add("X-Setup-Secret", SetupSecret);

        var response = await client.SendAsync(internalRequest);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }

    #region Helpers

    private static AuthenticatedWebApplicationFactory CreateFactoryWithSetupSecret()
    {
        return new OnboardingWebApplicationFactory();
    }

    private static AuthenticatedWebApplicationFactory CreateFactoryWithSetupSecret(
        IInstanceBootstrapAuditLogger auditLogger)
    {
        return new BootstrapAuditFactory(auditLogger);
    }

    private static async Task EnsureUserExistsAsync(AuthenticatedWebApplicationFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        var exists = await dbContext.Users.AnyAsync(x => x.Id == userId);
        if (exists) return;

        dbContext.Users.Add(new User { Id = userId, CreatedAt = DateTime.UtcNow,
        CreatedBy = userId,
        Pii = new UserPii
        {
            UserId = userId,
            Email = $"{userId:N}@integration.test",
            FirstName = "Test",
            LastName = "User"
        } });
        dbContext.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
            AuthenticationProvider = null!,
            ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                OnboardingWebApplicationFactory.Issuer, userId.ToString("D")).Value,
            ProviderDisplayName = "keycloak",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        });

        await dbContext.SaveChangesAsync();
    }

    private static HttpRequestMessage CreateInstanceAdminRequest(
        HttpMethod method, string url, Guid userId, object? body, bool includeSetupSecret)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Instance Admin",
                ("iss", OnboardingWebApplicationFactory.Issuer),
                ("idp", "keycloak")));

        if (includeSetupSecret)
            request.Headers.Add("X-Setup-Secret", SetupSecret);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return request;
    }

    private static CompleteInstanceOnboardingRequest CreateValidSettings()
    {
        return new CompleteInstanceOnboardingRequest
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Test Instance" },
            DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
            {
                PublicName = "Test Operator",
                LegalName = "Test Operator",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE",
                PublicContactEmail = "operator@integration.test",
                LegalNoticeUrl = "https://integration.test/legal",
                PrivacyUrl = "https://integration.test/privacy"
            },
            InstanceName = "Test Instance"
        };
    }

    private sealed class ValidateSecretResponse
    {
        public bool Valid { get; set; }
    }

    private sealed class BootstrapAuditFactory(IInstanceBootstrapAuditLogger auditLogger)
        : OnboardingWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IInstanceBootstrapAuditLogger>();
                services.AddSingleton(auditLogger);
            });
        }
    }

    private sealed class CapturingBootstrapAuditLogger : IInstanceBootstrapAuditLogger
    {
        private readonly List<InstanceBootstrapAuditEvent> _events = [];

        public IReadOnlyList<InstanceBootstrapAuditEvent> Events => _events;

        public void Log(InstanceBootstrapAuditEvent auditEvent)
        {
            _events.Add(auditEvent);
        }
    }

    #endregion
}

// ABOUTME: Isolates onboarding HTTP tests with real SQLite transactions and deployment-injected setup authority.
// ABOUTME: Keeps the shared registration/auth fixture unchanged and preserves request-scoped tenant filters.
internal class OnboardingWebApplicationFactory : AuthenticatedWebApplicationFactory
{
    internal const string Issuer = "https://auth.example.com";
    internal static string SetupSecret { get; } = RequireSecret("SETUP_SECRET");
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"onboarding-{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;

    internal static string RequireSecret(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Inject {key} from the documented test secret authority.");

    public OnboardingWebApplicationFactory()
    {
        AdditionalConfiguration["SETUP_SECRET"] = SetupSecret;
        ClientOptions.BaseAddress = new Uri("https://localhost");
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        }.ToString());
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            using (var context = new ExploreDbContext(options.Options))
            {
                context.Database.EnsureCreated();
            }

            services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
            services.AddScoped(provider =>
            {
                var context = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>()
                    .CreateDbContext();
                context.ClearTenantFilterBypass();
                context.TenantContext = provider.GetService<ITenantContext>();
                context.CurrentUserService = provider.GetService<ICurrentUserService>();
                return context;
            });
        });
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = _databasePath
        });
        options.UseSqlite(_connection).UseSnakeCaseNamingConvention();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            File.Delete(_databasePath);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
        File.Delete(_databasePath);
    }
}
