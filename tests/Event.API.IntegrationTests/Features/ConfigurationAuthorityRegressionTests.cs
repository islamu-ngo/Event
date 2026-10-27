extern alias bff;

using System.Collections.Concurrent;
using Event.Testing.DataProtection;
using Explore.API.Extensions;
using Explore.Application.Configuration;
using Explore.ServiceDefaults.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BffHosting = bff::Explore.Blazor.Hosting.BlazorHostServiceCollectionExtensions;
using BffProfile = bff::Explore.Blazor.Hosting.BlazorHostProfile;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class ConfigurationAuthorityRegressionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Api_InfisicalGlobalCredentialsCannotInheritLowerAuthority(bool present)
    {
        string username = Guid.CreateVersion7().ToString("N");
        string password = " " + Guid.CreateVersion7().ToString("N") + " ";
        await using var server = await SecretServer.StartAsync(path => path == "/database"
            ? present
                ? [new("DATABASE_GLOBAL_USERNAME", username, path), new("DATABASE_GLOBAL_PASSWORD", password, path)]
                : []
            : []);
        var builder = BootstrapInfisical(server.Url, Guid.CreateVersion7().ToString("N"), structured: false);
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_GLOBAL_USERNAME"] = Guid.CreateVersion7().ToString("N"),
            ["DATABASE_GLOBAL_PASSWORD"] = Guid.CreateVersion7().ToString("N"),
            ["Database:Global:Username"] = Guid.CreateVersion7().ToString("N"),
            ["Database:Global:Password"] = Guid.CreateVersion7().ToString("N"),
        });

        await using var owner = await OpenAuthorityAsync(builder.Build());
        await builder.AddPreparedApiConfigurationAsync(owner);
        var configuration = builder.Build();

        await Assert.That(configuration["Database:Global:Username"]).IsEqualTo(present ? username : null);
        await Assert.That(configuration["Database:Global:Password"]).IsEqualTo(present ? password : null);
        await Assert.That(configuration["DATABASE_GLOBAL_USERNAME"]).IsNull();
        await Assert.That(configuration["DATABASE_GLOBAL_PASSWORD"]).IsNull();
    }

    [Test]
    [Arguments("Environment")]
    [Arguments("UserSecrets")]
    public async Task Api_RemovedDurabilityInputCannotSilentlySelectEmbeddedAuthority(string provider)
    {
        var builder = Bootstrap(provider);
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PrivacyErasure__Durability__Mode"] = "ExternalDatabase",
        });
        using var authority = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(builder.Build(), "Testing", SecretConsumerRole.Api), authority);

        Func<Task> configure = async () => await builder.AddPreparedApiConfigurationAsync(owner);

        await Assert.That(configure).Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments("Environment", "ERASURE_DATABASE_TOPOLOGY", "ExternalDatabase", PrivacyErasureAuthorityTopology.ExternalDatabase)]
    [Arguments("Environment", "ERASURE_DATABASE_TOPOLOGY", "CoLocated", PrivacyErasureAuthorityTopology.CoLocated)]
    [Arguments("UserSecrets", "ERASURE_DATABASE_TOPOLOGY", "ExternalDatabase", PrivacyErasureAuthorityTopology.ExternalDatabase)]
    [Arguments("UserSecrets", "ERASURE_DATABASE_TOPOLOGY", "CoLocated", PrivacyErasureAuthorityTopology.CoLocated)]
    public async Task Api_PreservesExplicitSupportedTopology(string provider, string key, string value, PrivacyErasureAuthorityTopology expected)
    {
        using var authority = (ConfigurationRoot)new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        var builder = Bootstrap(provider);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(builder.Build(), "Testing", SecretConsumerRole.Api), authority);
        await builder.AddPreparedApiConfigurationAsync(owner);

        await Assert.That(PrivacyErasureDurabilityOptions.FromConfiguration(builder.Build()).Topology).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Environment")]
    [Arguments("UserSecrets")]
    public async Task Api_FlatProviderSelectionReachesSelectedRuntimeOwner(string provider)
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = provider,
        });
        using var authority = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(builder.Build(), "Testing", SecretConsumerRole.Api), authority);
        await builder.AddPreparedApiConfigurationAsync(owner);
        await using var host = RuntimeHost(owner);

        await Assert.That(ReferenceEquals(host.Services.GetRequiredService<SecretAuthorityAcquisition>(), owner)).IsTrue();
        await Assert.That(owner.Provider.ToString()).IsEqualTo(provider);
        await Assert.That(owner.Selection.Consumer).IsEqualTo(SecretConsumerRole.Api);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Api_InfisicalBootstrapAndPathsReachSelectedRuntimeOwner(bool structured)
    {
        await using var server = await SecretServer.StartAsync(path => path == "/database"
            ? [new("DATABASE_PROVIDER", "Sqlite", path)] : []);
        string credential = Guid.CreateVersion7().ToString("N");
        var builder = BootstrapInfisical(server.Url, credential, structured);
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Infisical:Paths:12"] = "/stale",
        });
        await using var owner = await OpenAuthorityAsync(builder.Build());
        await builder.AddPreparedApiConfigurationAsync(owner);
        await using var host = RuntimeHost(owner);
        InfisicalAuthorityOptions options = host.Services.GetRequiredService<SecretAuthorityAcquisition>().InfisicalOptions!;

        await Assert.That(owner.Provider).IsEqualTo(SecretAuthorityKind.Infisical);
        await Assert.That(options.Origin).IsEqualTo(new Uri(server.Url));
        await Assert.That(options.ProjectId).IsEqualTo("project-id");
        await Assert.That(options.ClientId).IsEqualTo("client-id");
        await Assert.That(server.LoginClientSecret).IsEqualTo(credential);
        await Assert.That(options.VaultEnvironment).IsEqualTo("testing");
        string[] expectedPaths =
        [
            "/runtime/api/authentication/keycloak", "/runtime/api/keycloak/lifecycle",
            "/runtime/api/authorization/cerbos",
            "/database", "/database/erasure", "/database/identity", "/postgresql",
            "/api", "/api/operator-identity", "/api/operatoridentity",
            "/api/bootstrap", "/api/instance-bootstrap", "/api/instancebootstrap",
            "/api/controlplane", "/api/control-plane", "/api/webpush", "/api/web-push",
            "/api/ratelimiting", "/api/rate-limiting",
            "/mcp", "/ai", "/storage", "/smtp", "/stripe", "/integrations/listmonk",
            "/setup", "/promotions", "/admissions", "/ticketing/recovery",
            "/atproto", "/webhook", "/messaging", "/reporting",
            "/registration-providers", "/analytics", "/localization"
        ];
        await Assert.That(SecretAcquisitionPolicy.Roots(owner.Selection.Consumer).SequenceEqual(expectedPaths)).IsTrue();
        string[] stagedPaths = [.. expectedPaths.Where(path => path is not ("/smtp" or "/analytics")), "/smtp", "/analytics"];
        await Assert.That(server.Paths.SequenceEqual(stagedPaths)).IsTrue();

        server.Paths.Clear();
        await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var runtimeOwner = host.Services.GetRequiredService<SecretAuthorityAcquisition>();
        var prepared = await runtimeOwner.PrepareConfigurationGroupAsync(expectedPaths);
        await Assert.That(prepared.IsSuccess).IsTrue();
        await Assert.That(prepared.Value["Database:Provider"]).IsEqualTo("Sqlite");
        await Assert.That(server.Paths.SequenceEqual(expectedPaths)).IsTrue();
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task Api_InfisicalModularSettingsReachTheirRuntimeConsumers()
    {
        await using var server = await SecretServer.StartAsync(path => path switch
        {
            "/api/operator-identity" => [new("PUBLIC_NAME", "Community Operator", path)],
            "/api/bootstrap" => [new("MODE", "configured", path)],
            "/api/webpush" => [new("VAPID_PUBLIC_KEY", "public-key", path), new("PUBLIC_KEY", "ignored-public-key", path)],
            "/api/ratelimiting" =>
            [
                new("ANONYMOUSREGISTRATION_IPPERMITLIMIT", "7", path),
                new("ANONYMOUSREGISTRATION__IPPERMITLIMIT", "11", path),
                new("RATELIMITING__ANONYMOUSREGISTRATION_IPPERMITLIMIT", "12", path)
            ],
            "/api/rate-limiting" => [new("RATE_LIMITING__ANONYMOUSREGISTRATION_IPPERMITLIMIT", "13", path)],
            _ => []
        });
        var builder = BootstrapInfisical(server.Url, Guid.CreateVersion7().ToString("N"), false);
        await using var owner = await OpenAuthorityAsync(builder.Build());
        await builder.AddPreparedApiConfigurationAsync(owner);
        var configuration = builder.Build();

        await Assert.That(configuration["Instance:OperatorIdentity:PublicName"]).IsEqualTo("Community Operator");
        await Assert.That(configuration["INSTANCE_BOOTSTRAP_MODE"]).IsEqualTo("configured");
        await Assert.That(configuration["WebPush:VapidPublicKey"]).IsEqualTo("public-key");
        await Assert.That(configuration["WebPush:PublicKey"]).IsNull();
        await Assert.That(configuration.GetValue<int>("RateLimiting:AnonymousRegistration:IpPermitLimit")).IsEqualTo(7);
    }

    [Test]
    public async Task Infisical_RecursiveResponseCannotPublishAnyConfiguration()
    {
        await using var server = await SecretServer.StartAsync(_ =>
        [
            new("DATABASE_HOST", "primary.example.test", "/database"),
            new("DATABASE_HOST", "child.example.test", "/database/child"),
            new("TOKEN", "first-value", "/database/one"),
            new("TOKEN", "second-value", "/database/two"),
        ]);
        var builder = BootstrapInfisical(server.Url, Guid.CreateVersion7().ToString("N"), false);
        await using var owner = await OpenAuthorityAsync(builder.Build());
        Func<Task> configure = async () => await builder.AddPreparedApiConfigurationAsync(owner);
        await Assert.That(configure).Throws<InvalidOperationException>();
        using var configuration = (ConfigurationRoot)builder.Build();
        string? primary = configuration["Database:Host"];
        string? first = configuration["Database:One:Token"];
        string? second = configuration["Database:Two:Token"];
        string? flatPrimary = configuration["DATABASE_HOST"];

        await Assert.That(primary).IsNull();
        await Assert.That(flatPrimary).IsNull();
        await Assert.That(first).IsNull();
        await Assert.That(second).IsNull();
    }

    [Test]
    public async Task Bff_RecursiveResponseOutsideExactPurposeFailsClosed()
    {
        await using var server = await SecretServer.StartAsync(_ =>
        [
            new("KEYCLOAK_BLAZOR_CLIENT_SECRET", Guid.CreateVersion7().ToString("N"),
                "/runtime/bff/oidc/keycloak/one"),
        ]);
        await using var owner = await OpenAuthorityAsync(
            BootstrapInfisical(server.Url, Guid.CreateVersion7().ToString("N"), false).Build(), SecretConsumerRole.Bff);
        var builder = FrontendHost();
        Func<Task> prepare = async () => await BffHosting.PrepareBlazorHostSecretsAsync(builder, BffProfile.Split, owner);
        var exception = await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("bff_secret_authority_unavailable");
        await Assert.That(builder.Configuration["Keycloak:ClientSecret"]).IsNull();
    }

    [Test]
    public async Task Bff_ExactPurposeResponseProjectsOnlyApprovedCredentialKeys()
    {
        string secret = Guid.CreateVersion7().ToString("N");
        var ring = new ExternalDataProtectionTestAuthority("bff").Input;
        using var environment = new EnvironmentScope(new()
        {
            ["EVENT_DP_BFF_ADMISSION_SHA256"] = ring.AdmissionSha256,
            ["EVENT_DP_BFF_MINIMUM_GENERATION"] = "1"
        });
        await using var server = await SecretServer.StartAsync(path => path switch
        {
            "/runtime/bff/oidc/keycloak" =>
            [
                new("KEYCLOAK_ENDPOINT", "https://identity.example.test", path),
                new("KEYCLOAK_REALM", "event", path),
                new("KEYCLOAK_CLIENT_ID", "event-bff", path),
                new("KEYCLOAK_BLAZOR_CLIENT_SECRET", secret, path)
            ],
            "/runtime/bff/data-protection" => [new("EVENT_DP_BFF_RING", ring.Ring, path)],
            "/admission/bff/data-protection" => [new("EVENT_DP_BFF_ADMISSION", ring.Admission, path)],
            _ => []
        });
        await using var owner = await OpenAuthorityAsync(
            BootstrapInfisical(server.Url, Guid.CreateVersion7().ToString("N"), false).Build(), SecretConsumerRole.Bff);
        var builder = FrontendHost();
        await BffHosting.PrepareBlazorHostSecretsAsync(builder, BffProfile.Split, owner);
        var configuration = builder.Configuration;
        await Assert.That(configuration["Keycloak:ClientSecret"]).IsEqualTo(secret);
        await Assert.That(configuration["KEYCLOAK_BLAZOR_CLIENT_SECRET"]).IsNull();
        await Assert.That(ReferenceEquals(builder.Services.Single(descriptor =>
            descriptor.ServiceType == typeof(SecretAuthorityAcquisition)).ImplementationInstance, owner)).IsTrue();
    }

    [Test]
    [Arguments("/")]
    [Arguments("/database")]
    [Arguments("/api")]
    [Arguments("/blazor")]
    public async Task Bff_BackendSecretRootsAreRejectedBeforeFetching(string path)
    {
        await using var server = await SecretServer.StartAsync(_ => []);
        Func<Task> prepare = async () =>
        {
            await using var owner = await OpenAuthorityAsync(
                BootstrapInfisical(server.Url, Guid.CreateVersion7().ToString("N"), false).Build(),
                SecretConsumerRole.Bff, path);
            await BffHosting.PrepareBlazorHostSecretsAsync(FrontendHost(), BffProfile.Split, owner);
        };

        await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(server.Paths).IsEmpty();
    }

    [Test]
    [Arguments("DATABASE_RUNTIME_PASSWORD")]
    [Arguments("ERASURE_DATABASE_MIGRATOR_PASSWORD")]
    [Arguments("Database__Runtime__Password")]
    [Arguments("KEYCLOAK_DB_PASSWORD")]
    public async Task Bff_DatabaseSecretsInsideFrontendFoldersFailClosed(string key)
    {
        string secret = Guid.CreateVersion7().ToString("N");
        await using var server = await SecretServer.StartAsync(_ =>
            [new(key, secret, "/runtime/bff/oidc/keycloak")]);
        await using var owner = await OpenAuthorityAsync(
            BootstrapInfisical(server.Url, Guid.CreateVersion7().ToString("N"), false).Build(), SecretConsumerRole.Bff);
        var builder = FrontendHost();

        Func<Task> prepare = async () => await BffHosting.PrepareBlazorHostSecretsAsync(builder, BffProfile.Split, owner);
        var exception = await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("bff_secret_authority_unavailable");
        await Assert.That(exception!.Message).DoesNotContain(secret);
        await Assert.That(builder.Configuration[key]).IsNull();
        await Assert.That(builder.Configuration["Keycloak:BlazorClientSecret"]).IsNull();
    }

    private static ConfigurationBuilder Bootstrap(string provider) =>
        (ConfigurationBuilder)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = provider,
        });

    private static WebApplication RuntimeHost(SecretAuthorityAcquisition owner)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.Sources.Clear();
        builder.Services.AddSingleton(owner);
        return builder.Build();
    }

    private static WebApplicationBuilder FrontendHost()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        return builder;
    }

    private static ValueTask<SecretAuthorityAcquisition> OpenAuthorityAsync(
        IConfiguration bootstrap, SecretConsumerRole consumer = SecretConsumerRole.Api, string? folder = null) =>
        SecretAuthorityBootstrap.OpenAsync(bootstrap, "Testing", consumer,
            [new(folder ?? SecretAcquisitionPolicy.Roots(consumer)[0], "ADMITTED", "Admitted")], new());

    private static ConfigurationBuilder BootstrapInfisical(string url, string credential, bool structured)
    {
        var values = new Dictionary<string, string?>
        {
            ["SecretProvider:Infisical:TokenLifetime"] = "Finite"
        };
        foreach (var (name, flat, value) in new[]
        {
            ("Url", "INFISICAL_URL", url), ("ProjectId", "INFISICAL_PROJECT_ID", "project-id"),
            ("ClientId", "INFISICAL_CLIENT_ID", "client-id"), ("ClientSecret", "INFISICAL_CLIENT_SECRET", credential),
            ("Environment", "INFISICAL_ENV", "testing"),
        })
        {
            values[$"SecretProvider:Infisical:{name}"] = structured ? value : null;
            values[flat] = structured ? null : value;
        }
        var builder = Bootstrap("Infisical");
        builder.AddInMemoryCollection(values);
        return builder;
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous;
        public EnvironmentScope(Dictionary<string, string?> values)
        {
            _previous = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
            foreach (var pair in values) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
        public void Dispose()
        {
            foreach (var pair in _previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private sealed record RawSecret(string secretKey, string secretValue, string secretPath);

    private sealed class SecretServer(WebApplication app) : IAsyncDisposable
    {
        public string Url => app.Urls.Single();
        public ConcurrentQueue<string> Paths { get; } = new();
        public string? LoginClientSecret { get; private set; }
        public static async Task<SecretServer> StartAsync(Func<string, RawSecret[]> response)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var server = new SecretServer(app);
            string token = Guid.CreateVersion7().ToString("N");
            var identities = new ConcurrentDictionary<(string Path, string Key), string>();
            app.MapPost("/api/v1/auth/universal-auth/login", async (HttpContext context) =>
            {
                using var body = await System.Text.Json.JsonDocument.ParseAsync(
                    context.Request.Body, cancellationToken: context.RequestAborted);
                server.LoginClientSecret = body.RootElement.GetProperty("clientSecret").GetString();
                return Results.Json(new { accessToken = token, accessTokenMaxTTL = 3600, expiresIn = 600, tokenType = "Bearer" });
            });
            object Observation(RawSecret secret, HttpContext context, bool hidden)
            {
                string id = identities.GetOrAdd((secret.secretPath, secret.secretKey), _ => Guid.CreateVersion7().ToString("N"));
                string workspace = context.Request.Query["projectId"].ToString();
                string environment = context.Request.Query["environment"].ToString();
                if (hidden)
                    return new
                    {
                        id, workspace, environment, secret.secretPath, secret.secretKey,
                        secretValueHidden = true, type = "shared", version = 1
                    };
                return new
                {
                    id, workspace, environment, secret.secretPath, secret.secretKey, secret.secretValue,
                    secretValueHidden = false, type = "shared", version = 1
                };
            }
            app.MapGet("/api/v4/secrets", (HttpContext context) =>
            {
                string path = context.Request.Query["secretPath"].ToString();
                server.Paths.Enqueue(path);
                return Results.Json(new { secrets = response(path).Select(secret => Observation(secret, context, hidden: true)).ToArray() });
            });
            app.MapGet("/api/v4/secrets/{key}", (HttpContext context) =>
            {
                string path = context.Request.Query["secretPath"].ToString();
                string key = (string)context.Request.RouteValues["key"]!;
                RawSecret[] secrets = response(path);
                // Hostile test responses retain their wrong path/key instead of being
                // repaired by the fake before native response validation can see them.
                RawSecret? secret = secrets.FirstOrDefault(item => item.secretKey == key) ?? secrets.FirstOrDefault();
                return secret is null ? Results.NotFound()
                    : Results.Json(new { secret = Observation(secret, context, hidden: false) });
            });
            await app.StartAsync().WaitAsync(TimeSpan.FromSeconds(15));
            return server;
        }
        public async ValueTask DisposeAsync()
        {
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await app.DisposeAsync();
        }
    }
}
