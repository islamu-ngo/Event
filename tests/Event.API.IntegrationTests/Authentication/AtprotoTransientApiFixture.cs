// ABOUTME: Hosts the real API authentication pipeline and P1 stores on migrated PostgreSQL.
// ABOUTME: Supplies ephemeral signing authority through the external secret resolver and restores production auth dispatch.

extern alias bff;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.Domain;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;
using BffAuth = bff::Explore.Blazor.Services.Auth;
using BffHttpClients = bff::Explore.Blazor.Extensions.HttpClientExtensions;

namespace Event.API.IntegrationTests.Authentication;

public sealed class AtprotoTransientApiFixture : IAsyncInitializer, IAsyncDisposable
{
    public const string Prefix = "/api/auth/atproto/transient/";
    public const string Header = "X-Atproto-Transient-Assertion";
    public const string Issuer = "event-atproto-transient-bff";
    public const string Audience = "event-atproto-transient-api";
    public const string Use = "atproto-transient";
    public const string Purpose = "oauth_state";
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa retiringKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly PostgreSqlContainer container;
    public ISecretResolver Secrets { get; } = Substitute.For<ISecretResolver>();
    public PostgreSqlApiWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public FrozenClock Clock { get; } = new();
    public Func<CancellationToken, Task>? BeforeReplayInsert { get; set; }

    public AtprotoTransientApiFixture()
    {
        ECParameters parameters = key.ExportParameters(true);
        ECParameters retiring = retiringKey.ExportParameters(true);
        string ring = JsonSerializer.Serialize(new { keys = new[] { new {
            kty = "EC", crv = "P-256", kid = "transient-test", use = "sig", alg = "ES256", status = "active",
            x = Base64UrlEncoder.Encode(parameters.Q.X!), y = Base64UrlEncoder.Encode(parameters.Q.Y!),
            d = Base64UrlEncoder.Encode(parameters.D!) }, new {
            kty = "EC", crv = "P-256", kid = "transient-retiring", use = "sig", alg = "ES256", status = "retired",
            x = Base64UrlEncoder.Encode(retiring.Q.X!), y = Base64UrlEncoder.Encode(retiring.Q.Y!),
            d = Base64UrlEncoder.Encode(retiring.D!) } } });
        Secrets.ResolveAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(call =>
            SecretResolutionResult.Resolved(new ResolvedSecret(call.ArgAt<string>(0),
                call.ArgAt<string>(0) == SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks
                    ? ring : Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                SecretSourceType.Infisical, SecretScope.Instance, null, Clock.GetUtcNow())));
        // The test secret authority, not an inline credential, owns container authentication.
        string password = Secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Postgresql.Password, null, default)
            .GetAwaiter().GetResult().Secret!.Value;
        container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("atproto_transient_api").WithUsername("postgres").WithPassword(password).Build();
    }

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        var configuration = new Dictionary<string, string?>();
        TestDatabaseConfiguration.AddPostgreSql(configuration, container.GetConnectionString());
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            PrimaryDatabaseConfiguration.BindRuntime(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build()));
        await using (var db = new ExploreDbContext(options.Options))
        {
            await db.Database.MigrateAsync();
            await LookupTableSeeder.SeedAsync(db);
        }
        Factory = new PostgreSqlApiWebApplicationFactory(container.GetConnectionString(), new()
        {
            ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
            ["RateLimiting:DisableInTesting"] = "true",
            ["Deployment:Mode"] = "MultiTenant"
        }, ConfigureServices);
        Client = Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ISecretResolver>();
        services.AddSingleton(Secrets);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(Clock);
        services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(new ReplayInsertInterceptor(this)));
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = ApiAuthenticationSchemeNames.MultiAuth;
            options.DefaultAuthenticateScheme = ApiAuthenticationSchemeNames.MultiAuth;
            options.DefaultChallengeScheme = ApiAuthenticationSchemeNames.MultiAuth;
            options.DefaultForbidScheme = ApiAuthenticationSchemeNames.MultiAuth;
            options.DefaultSignInScheme = null;
            options.DefaultSignOutScheme = null;
        });
    }

    public async Task<ServiceProvider> CreateBffServicesAsync(HttpMessageHandler? handler = null, bool rotateKeys = false,
        bool globalHedging = false, ILoggerProvider? logs = null, TimeProvider? timeProvider = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ExploreApi:BaseUrl"] = "https://api.test" });
        if (logs is not null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddFilter((_, _) => true);
            builder.Logging.AddProvider(logs);
        }
        // Deliberately install global resilience first: the actual private-client registration must remove it.
        builder.Services.ConfigureHttpClientDefaults(client =>
        {
            if (globalHedging) client.AddStandardHedgingHandler();
            else client.AddStandardResilienceHandler();
        });
        BffHttpClients.AddApiHttpClients(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.AddHttpClient(BffAuth.ApiBackedAtprotoTransientStore.HttpClientName)
            .ConfigureHttpClient(client => client.BaseAddress = new Uri("https://api.test"))
            .ConfigurePrimaryHttpMessageHandler(() => handler ?? Factory.Server.CreateHandler());
        var authority = await Secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks, null, default);
        var ring = JsonNode.Parse(authority.Value!)!;
        if (rotateKeys)
        {
            foreach (var key in ring["keys"]!.AsArray())
                key!["status"] = key["status"]!.GetValue<string>() == "active" ? "retired" : "active";
        }
        builder.Services.AddSingleton(new BffAuth.AtprotoClientKeyProvider(Options.Create(new BffAuth.AtprotoClientKeyOptions
        {
            OAuthClientPrivateJwks = ring.ToJsonString()
        })));
        builder.Services.AddSingleton<TimeProvider>(timeProvider ?? Clock);
        builder.Services.AddSingleton<BffAuth.AtprotoTransientAssertionService>();
        builder.Services.AddSingleton<BffAuth.ApiBackedAtprotoTransientStore>();
        return builder.Services.BuildServiceProvider();
    }

    public byte[] ReadBody(string? digest = null, string purpose = Purpose, Guid? tenantId = null) =>
        JsonSerializer.SerializeToUtf8Bytes(new { purpose, tokenDigest = digest ?? NewDigest(), expectedTenantId = tenantId });

    public static string NewDigest() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public string Sign(byte[] body, string operation = "read", Action<Dictionary<string, object>>? mutate = null,
        Action<Dictionary<string, object>>? mutateHeader = null, Func<string, string>? payloadTransform = null,
        Func<string, string>? headerTransform = null, bool useRetiringKey = false)
    {
        using var bodyDocument = JsonDocument.Parse(body);
        var claims = new Dictionary<string, object>
        {
            ["iss"] = Issuer, ["aud"] = Audience, ["sub"] = "event-blazor-bff", ["use"] = Use,
            ["jti"] = Guid.CreateVersion7().ToString("D"), ["iat"] = Clock.GetUtcNow().ToUnixTimeSeconds(),
            ["exp"] = Clock.GetUtcNow().AddSeconds(30).ToUnixTimeSeconds(), ["method"] = "POST",
            ["path"] = Prefix + operation, ["operation"] = operation,
            ["purpose"] = bodyDocument.RootElement.GetProperty("purpose").GetString()!,
            ["body_sha256"] = Convert.ToHexStringLower(SHA256.HashData(body))
        };
        var header = new Dictionary<string, object> { ["alg"] = "ES256", ["kid"] = useRetiringKey ? "transient-retiring" : "transient-test", ["typ"] = "JWT" };
        mutate?.Invoke(claims);
        mutateHeader?.Invoke(header);
        string headerJson = JsonSerializer.Serialize(header);
        string claimsJson = JsonSerializer.Serialize(claims);
        string input = Base64UrlEncoder.Encode(headerTransform?.Invoke(headerJson) ?? headerJson) + "."
            + Base64UrlEncoder.Encode(payloadTransform?.Invoke(claimsJson) ?? claimsJson);
        byte[] signature = (useRetiringKey ? retiringKey : key).SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return input + "." + Base64UrlEncoder.Encode(signature);
    }

    public HttpRequestMessage Request(byte[] body, string? assertion, string operation = "read")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Prefix + operation) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        if (assertion is not null) request.Headers.TryAddWithoutValidation(Header, assertion);
        return request;
    }

    public async Task<Guid> SeedTenantAsync(bool enabled = true)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(status => status.Id == (int)(enabled ? TenantStatusEnum.Active : TenantStatusEnum.Suspended));
        Guid id = Guid.CreateVersion7();
        db.Tenants.Add(new Explore.Domain.Tenant
        {
            Id = id, FullName = "Transient test tenant", Slug = "transient-" + id.ToString("N"),
            TenantStatus = status, TenantStatusId = status.Id, CreatedAt = Clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync();
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null) await Factory.DisposeAsync();
        await container.DisposeAsync();
        key.Dispose();
        retiringKey.Dispose();
    }

    public sealed class FrozenClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ReplayInsertInterceptor(AtprotoTransientApiFixture fixture) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (fixture.BeforeReplayInsert is { } beforeInsert
                && eventData.Context!.ChangeTracker.Entries<AtprotoTransientAssertionReplay>().Any(entry => entry.State == EntityState.Added))
                await beforeInsert(cancellationToken);
            return result;
        }
    }
}
