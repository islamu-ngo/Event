extern alias bff;

using System.Collections.Concurrent;
using Explore.API.Extensions;
using Explore.Application.Configuration;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BffSource = bff::Explore.Blazor.Configuration.InfisicalConfigurationSource;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class ConfigurationAuthorityRegressionTests
{
    [Test]
    public async Task Api_RemovedDurabilityInputCannotSilentlySelectEmbeddedAuthority()
    {
        using var environment = new EnvironmentScope(new()
        {
            ["PrivacyErasure__Durability__Mode"] = "ExternalDatabase",
        });
        var builder = Bootstrap("Environment");

        Action configure = () => builder.AddSecretAuthorityConfiguration("Testing");

        await Assert.That(configure).Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments("ERASURE_DATABASE_TOPOLOGY", "ExternalDatabase", PrivacyErasureAuthorityTopology.ExternalDatabase)]
    [Arguments("ERASURE_DATABASE_TOPOLOGY", "CoLocated", PrivacyErasureAuthorityTopology.CoLocated)]
    public async Task Api_PreservesExplicitSupportedTopology(string key, string value, PrivacyErasureAuthorityTopology expected)
    {
        using var environment = new EnvironmentScope(new() { [key] = value });
        var builder = Bootstrap("Environment");
        builder.AddSecretAuthorityConfiguration("Testing");

        await Assert.That(PrivacyErasureDurabilityOptions.FromConfiguration(builder.Build()).Topology).IsEqualTo(expected);
    }

    [Test]
    public async Task Api_FlatProviderSelectionReachesValidatedRuntimeOptions()
    {
        using var environment = new EnvironmentScope(new() { ["SECRET_PROVIDER"] = "Environment" });
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "Environment",
        });
        IConfiguration providerConfiguration = builder.AddSecretAuthorityConfiguration("Testing");
        await using var host = RuntimeHost(builder.Build(), providerConfiguration);

        await Assert.That(host.Services.GetRequiredService<IOptions<SecretProviderOptions>>().Value.Provider)
            .IsEqualTo(SecretProviderType.Environment);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Api_InfisicalBootstrapAndPathsReachValidatedRuntimeOptions(bool structured)
    {
        await using var server = await SecretServer.StartAsync(_ => []);
        string credential = Guid.CreateVersion7().ToString("N");
        using var environment = BootstrapEnvironment(server.Url, credential, structured);
        var builder = Bootstrap("Infisical");
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Infisical:Paths:12"] = "/stale",
        });
        IConfiguration providerConfiguration = builder.AddSecretAuthorityConfiguration("Testing");
        await using var host = RuntimeHost(builder.Build(), providerConfiguration);
        SecretProviderOptions options = host.Services.GetRequiredService<IOptions<SecretProviderOptions>>().Value;

        await Assert.That(options.Provider).IsEqualTo(SecretProviderType.Infisical);
        await Assert.That(options.Infisical.Url).IsEqualTo(server.Url);
        await Assert.That(options.Infisical.ProjectId).IsEqualTo("project-id");
        await Assert.That(options.Infisical.ClientId).IsEqualTo("client-id");
        await Assert.That(options.Infisical.ClientSecret).IsEqualTo(credential);
        await Assert.That(options.Infisical.Environment).IsEqualTo("testing");
        string[] expectedPaths =
        [
            "/keycloak", "/database", "/database/erasure", "/database/identity", "/api",
            "/cerbos", "/mcp", "/ai", "/storage", "/smtp", "/stripe", "/integrations/listmonk",
        ];
        await Assert.That(options.Infisical.Paths.SequenceEqual(expectedPaths)).IsTrue();

        server.Paths.Clear();
        await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var provider = host.Services.GetRequiredService<ISecretProvider>();
        await Assert.That((await provider.GetHealthAsync()).IsHealthy).IsTrue();
        await Assert.That(server.Paths.SequenceEqual(expectedPaths)).IsTrue();
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task Api_InfisicalModularSettingsReachTheirRuntimeConsumers()
    {
        await using var server = await SecretServer.StartAsync(path => path == "/api"
            ? [
                new("PUBLIC_NAME", "Community Operator", "/api/operator-identity"),
                new("MODE", "configured", "/api/bootstrap"),
                new("PUBLIC_KEY", "public-key", "/api/webpush"),
                new("ANONYMOUSREGISTRATION__IPPERMITLIMIT", "7", "/api/ratelimiting"),
            ]
            : []);
        using var environment = BootstrapEnvironment(server.Url, Guid.CreateVersion7().ToString("N"), false);
        var builder = Bootstrap("Infisical");
        builder.AddSecretAuthorityConfiguration("Testing");
        var configuration = builder.Build();

        await Assert.That(configuration["Instance:OperatorIdentity:PublicName"]).IsEqualTo("Community Operator");
        await Assert.That(configuration["INSTANCE_BOOTSTRAP_MODE"]).IsEqualTo("configured");
        await Assert.That(configuration["WebPush:VapidPublicKey"]).IsEqualTo("public-key");
        await Assert.That(configuration.GetValue<int>("RateLimiting:AnonymousRegistration:IpPermitLimit")).IsEqualTo(7);
    }

    [Test]
    public async Task Infisical_RecursiveResponseRetainsSiblingNamespacesAndCannotOverwritePrimaryDatabase()
    {
        await using var server = await SecretServer.StartAsync(_ =>
        [
            new("DATABASE_HOST", "primary.example.test", "/database"),
            new("DATABASE_HOST", "child.example.test", "/database/child"),
            new("TOKEN", "first-value", "/database/one"),
            new("TOKEN", "second-value", "/database/two"),
        ]);
        using var source = new InfisicalConfigurationProvider(new()
        {
            Url = server.Url,
            ProjectId = "project-id",
            ClientId = "client-id",
            ClientSecret = Guid.CreateVersion7().ToString("N"),
            Environment = "testing",
            Paths = ["/database"],
        });
        source.Load();
        source.TryGet("Database:Host", out string? primary);
        source.TryGet("Database:One:Token", out string? first);
        source.TryGet("Database:Two:Token", out string? second);
        source.TryGet("DATABASE_HOST", out string? flatPrimary);

        await Assert.That(primary).IsEqualTo("primary.example.test");
        await Assert.That(flatPrimary).IsEqualTo("primary.example.test");
        await Assert.That(first).IsEqualTo("first-value");
        await Assert.That(second).IsEqualTo("second-value");
    }

    [Test]
    public async Task Bff_RecursiveResponseRetainsNestedNamespaces()
    {
        await using var server = await SecretServer.StartAsync(_ =>
        [
            new("TOKEN", "first-value", "/blazor/one"),
            new("TOKEN", "second-value", "/blazor/two"),
        ]);
        var source = FrontendSource(server.Url, "/blazor");
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Add(source).Build();

        await Assert.That(configuration["Blazor:One:Token"]).IsEqualTo("first-value");
        await Assert.That(configuration["Blazor:Two:Token"]).IsEqualTo("second-value");
        await Assert.That(configuration["TOKEN"]).IsNull();
    }

    [Test]
    [Arguments("/")]
    [Arguments("/database")]
    [Arguments("/api")]
    public async Task Bff_BackendSecretRootsAreRejectedBeforeFetching(string path)
    {
        await using var server = await SecretServer.StartAsync(_ => []);
        var builder = new ConfigurationBuilder().Add(FrontendSource(server.Url, path));

        await Assert.That(() => builder.Build()).Throws<InvalidOperationException>();
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
        await using var server = await SecretServer.StartAsync(_ => [new(key, secret, "/blazor")]);
        var builder = new ConfigurationBuilder().Add(FrontendSource(server.Url, "/blazor"));

        var exception = await Assert.That(() => builder.Build()).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).DoesNotContain(secret);
    }

    private static ConfigurationBuilder Bootstrap(string provider) =>
        (ConfigurationBuilder)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = provider,
        });

    private static WebApplication RuntimeHost(IConfiguration configuration, IConfiguration providerConfiguration)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSecretManagement(configuration, providerConfiguration,
            enableAuditing: true, enableRefreshService: false, enableSecretResolution: false);
        return builder.Build();
    }

    private static BffSource FrontendSource(string url, string path)
    {
        var source = new BffSource
        {
            Url = url,
            ProjectId = "project-id",
            ClientId = "client-id",
            ClientSecret = Guid.CreateVersion7().ToString("N"),
            Environment = "testing",
        };
        source.Paths.Clear();
        source.Paths.Add(path);
        return source;
    }

    private static EnvironmentScope BootstrapEnvironment(string url, string credential, bool structured)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (name, flat, value) in new[]
        {
            ("Url", "INFISICAL_URL", url), ("ProjectId", "INFISICAL_PROJECT_ID", "project-id"),
            ("ClientId", "INFISICAL_CLIENT_ID", "client-id"), ("ClientSecret", "INFISICAL_CLIENT_SECRET", credential),
            ("Environment", "INFISICAL_ENV", "testing"),
        })
        {
            values[$"SecretProvider__Infisical__{name}"] = structured ? value : null;
            values[flat] = structured ? null : value;
        }
        return new(values);
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
        public static async Task<SecretServer> StartAsync(Func<string, RawSecret[]> response)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var server = new SecretServer(app);
            string token = Guid.CreateVersion7().ToString("N");
            app.MapPost("/api/v1/auth/universal-auth/login", () => Results.Json(new { accessToken = token }));
            app.MapGet("/api/v3/secrets/raw", (HttpContext context) =>
            {
                string path = context.Request.Query["secretPath"].ToString();
                server.Paths.Enqueue(path);
                return Results.Json(new { secrets = response(path) });
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
