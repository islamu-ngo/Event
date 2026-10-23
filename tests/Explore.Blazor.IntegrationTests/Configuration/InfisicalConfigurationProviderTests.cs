using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Explore.Blazor.Configuration;
using Explore.Blazor.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.IntegrationTests.Configuration;

[NotInParallel]
public sealed class InfisicalConfigurationProviderTests
{
    [Test]
    public async Task AcquiredOidcSettingsSurviveStartupProjection()
    {
        string keycloakSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string googleSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string accessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var roots = new Dictionary<string, Dictionary<string, string>>
        {
            ["/runtime/bff/oidc/keycloak"] = new()
            {
                ["KEYCLOAK_ENDPOINT"] = "https://identity.example.test",
                ["KEYCLOAK_REALM"] = "operators",
                ["KEYCLOAK_CLIENT_ID"] = "replacement-keycloak-client",
                ["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = keycloakSecret
            },
            ["/runtime/bff/oidc/google"] = new()
            {
                ["GOOGLE_CLIENT_ID"] = "replacement-google-client",
                ["GOOGLE_CLIENT_SECRET"] = googleSecret
            },
            ["/runtime/bff/atproto/oauth"] = new()
        };
        var host = WebApplication.CreateBuilder();
        host.Logging.ClearProviders();
        await using var authority = host.Build();
        authority.Urls.Add("http://127.0.0.1:0");
        authority.MapPost("/api/v1/auth/universal-auth/login",
            () => Results.Json(new { accessToken }));
        authority.MapGet("/api/v3/secrets/raw", (HttpRequest request) =>
        {
            string path = request.Query["secretPath"].ToString();
            return Results.Json(new
            {
                secrets = roots[path].Select(secret => new
                {
                    secretPath = path,
                    secretKey = secret.Key,
                    secretValue = secret.Value
                }).ToArray()
            });
        });
        await authority.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var bootstrap = new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Infisical",
            ["SecretProvider:Infisical:Url"] = authority.Urls.Single(),
            ["SecretProvider:Infisical:ProjectId"] = Guid.CreateVersion7().ToString("N"),
            ["SecretProvider:Infisical:ClientId"] = Guid.CreateVersion7().ToString("N"),
            ["SecretProvider:Infisical:ClientSecret"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["SecretProvider:Infisical:Environment"] = "development"
        };
        var previous = bootstrap.Keys
            .Where(key => key.StartsWith("SecretProvider:Infisical:", StringComparison.Ordinal))
            .ToDictionary(key => key.Replace(":", "__", StringComparison.Ordinal),
                key => Environment.GetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal)));
        try
        {
            foreach (var setting in bootstrap.Where(setting =>
                         setting.Key.StartsWith("SecretProvider:Infisical:", StringComparison.Ordinal)))
                Environment.SetEnvironmentVariable(setting.Key.Replace(":", "__", StringComparison.Ordinal), setting.Value);
            var application = new ConfigurationBuilder().AddInMemoryCollection(bootstrap);
            Guid profileRevision = Guid.CreateVersion7();
            var receipts = new AuthenticationCredentialAcquisitionReceipts(profileRevision);
            application.AddSecretAuthorityConfiguration("Testing", receipts);
            IConfiguration configuration = application.Build();
            var expected = new Dictionary<string, string>
            {
                ["Keycloak:Realm"] = "operators",
                ["Keycloak:ClientId"] = "replacement-keycloak-client",
                ["Keycloak:Authority"] = "https://identity.example.test/realms/operators",
                ["Keycloak:ClientSecret"] = keycloakSecret,
                ["Google:ClientId"] = "replacement-google-client",
                ["Google:ClientSecret"] = googleSecret
            };
            foreach (var setting in expected)
                await Assert.That(configuration[setting.Key] == setting.Value).IsTrue()
                    .Because($"Acquired {setting.Key} must survive application startup projection.");
            var acquired = receipts.Snapshot();
            await Assert.That(acquired.Count).IsEqualTo(2);
            await Assert.That(acquired.All(receipt => receipt.ProcessBootId == receipts.ProcessBootId
                && receipt.Authority == AuthenticationCredentialAcquisitionReceipts.Authority.Infisical
                && receipt.AuthorityProfileRevision == profileRevision
                && !receipt.Installed && receipt.Generation != Guid.Empty)).IsTrue();
            string serialized = System.Text.Json.JsonSerializer.Serialize(acquired);
            await Assert.That(serialized.Contains(keycloakSecret, StringComparison.Ordinal)
                || serialized.Contains(googleSecret, StringComparison.Ordinal)
                || serialized.Contains(accessToken, StringComparison.Ordinal)).IsFalse();
        }
        finally
        {
            foreach (var setting in previous)
                Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }
    }

    [Test]
    [Arguments("/keycloak")]
    [Arguments("/blazor")]
    [Arguments("/atproto")]
    [Arguments("/runtime/bff/oidc/keycloak/nested")]
    [Arguments("/runtime/bff/oidc/google/nested")]
    public async Task NonOidcRootIsRejectedBeforeAuthorityAuthentication(string path)
    {
        using var transport = new AuthorityTransport();
        var source = CreateSource(transport, path);
        var provider = new InfisicalConfigurationProvider(source);
        InvalidOperationException? rejection = null;
        try
        {
            provider.Load();
        }
        catch (InvalidOperationException exception)
        {
            rejection = exception;
        }

        await Assert.That(transport.Requests.Count).IsEqualTo(0);
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task OidcAcquisitionUsesOnlyExactRootsWithoutReferenceExpansion()
    {
        using var transport = new AuthorityTransport();
        var source = CreateSource(transport,
            "/runtime/bff/oidc/keycloak", "/runtime/bff/oidc/google");
        var provider = new InfisicalConfigurationProvider(source);

        provider.Load();

        Uri[] acquisitions = transport.Requests.Where(request =>
            request.AbsolutePath == "/api/v3/secrets/raw").ToArray();
        await Assert.That(acquisitions.Length).IsEqualTo(2);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (Uri acquisition in acquisitions)
        {
            var query = QueryHelpers.ParseQuery(acquisition.Query);
            paths.Add(query["secretPath"].ToString());
            await Assert.That(query["recursive"].ToString()).IsEqualTo("false");
            await Assert.That(query["expandSecretReferences"].ToString()).IsEqualTo("false");
        }
        await Assert.That(paths.SetEquals(
            ["/runtime/bff/oidc/keycloak", "/runtime/bff/oidc/google"])).IsTrue();
    }

    [Test]
    [Arguments("/runtime/bff/oidc/google/nested", "GOOGLE_CLIENT_SECRET", "generated")]
    [Arguments("/runtime/bff/oidc/google", "GOOGLE_CLIENT_SECRET", "${backend.JWT_SIGNING_KEY}")]
    [Arguments("/runtime/bff/oidc/google", "JWT_SIGNING_KEY", "generated")]
    [Arguments(null, "GOOGLE_CLIENT_SECRET", "generated")]
    [Arguments("/runtime/bff/oidc/google", "KEYCLOAK_BLAZOR_CLIENT_SECRET", "generated")]
    public async Task InvalidReturnedProvenanceOrMaterialCannotEnterConfiguration(
        string? returnedPath, string key, string value)
    {
        string material = value == "generated"
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            : value;
        using var transport = new AuthorityTransport(returnedPath: returnedPath, returnedKey: key, returnedValue: material);
        var provider = new InfisicalConfigurationProvider(CreateSource(transport, "/runtime/bff/oidc/google"));
        InvalidOperationException? rejection = null;
        try { provider.Load(); }
        catch (InvalidOperationException exception) { rejection = exception; }

        await Assert.That(transport.Requests.Count).IsEqualTo(2);
        await Assert.That(rejection).IsNotNull();
        await Assert.That(provider.TryGet("Google:ClientSecret", out _)).IsFalse();
        await Assert.That(provider.TryGet("Keycloak:BlazorClientSecret", out _)).IsFalse();
    }

    [Test]
    public async Task DuplicateAcquiredCredentialCannotBePartiallyPublished()
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var transport = new AuthorityTransport(
            returnedPath: "/runtime/bff/oidc/google",
            returnedKey: "GOOGLE_CLIENT_SECRET",
            returnedValue: canary,
            duplicateSecrets: true);
        var provider = new InfisicalConfigurationProvider(
            CreateSource(transport, "/runtime/bff/oidc/google"));
        InvalidOperationException? rejection = null;
        try { provider.Load(); }
        catch (InvalidOperationException exception) { rejection = exception; }

        await Assert.That(rejection).IsNotNull();
        await Assert.That(provider.TryGet("Google:ClientSecret", out _)).IsFalse();
    }

    [Test]
    public async Task DedicatedAtprotoAcquisitionPreservesOnlyFrontendSigningKey()
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var transport = new AuthorityTransport(
            returnedPath: "/runtime/bff/atproto/oauth",
            returnedKey: "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS", returnedValue: canary);
        var provider = new InfisicalConfigurationProvider(
            CreateSource(transport, "/runtime/bff/atproto/oauth"));
        provider.Load();

        await Assert.That(provider.TryGet("Atproto:OauthClientPrivateJwks", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(canary);
        Uri acquisition = transport.Requests.Single(request => request.AbsolutePath == "/api/v3/secrets/raw");
        var query = QueryHelpers.ParseQuery(acquisition.Query);
        await Assert.That(query["secretPath"].ToString()).IsEqualTo("/runtime/bff/atproto/oauth");
        await Assert.That(query["recursive"].ToString()).IsEqualTo("false");
        await Assert.That(query["expandSecretReferences"].ToString()).IsEqualTo("false");
    }

    [Test]
    public async Task SelectedAuthorityRejectionStopsStartup()
    {
        using var transport = new AuthorityTransport(rejectAuthentication: true);
        var source = CreateSource(transport, "/runtime/bff/oidc/google");
        var provider = new InfisicalConfigurationProvider(source);
        InvalidOperationException? failure = null;
        try
        {
            provider.Load();
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        await Assert.That(failure).IsNotNull();
    }

    private static InfisicalConfigurationSource CreateSource(
        AuthorityTransport transport, params string[] paths)
    {
        var source = new InfisicalConfigurationSource
        {
            Url = "https://authority.integration.test",
            ProjectId = Guid.CreateVersion7().ToString("N"),
            ClientId = Guid.CreateVersion7().ToString("N"),
            ClientSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Environment = "development",
            MessageHandlerFactory = () => transport
        };
        source.Paths.Clear();
        source.Paths.AddRange(paths);
        return source;
    }

    private sealed class AuthorityTransport(bool rejectAuthentication = false,
        string? returnedPath = null, string? returnedKey = null, string? returnedValue = null,
        bool duplicateSecrets = false) : HttpMessageHandler
    {
        private readonly string _accessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri uri = request.RequestUri ?? throw new InvalidOperationException("Missing authority URI.");
            Requests.Add(uri);
            if (request.Method == HttpMethod.Post &&
                uri.AbsolutePath == "/api/v1/auth/universal-auth/login")
            {
                return Task.FromResult(new HttpResponseMessage(
                    rejectAuthentication ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { accessToken = _accessToken })
                });
            }
            if (request.Method == HttpMethod.Get && uri.AbsolutePath == "/api/v3/secrets/raw")
            {
                object secret = new
                {
                    secretPath = returnedPath,
                    secretKey = returnedKey,
                    secretValue = returnedValue
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        secrets = returnedKey is null ? Array.Empty<object>() :
                            duplicateSecrets ? [secret, secret] : [secret]
                    })
                });
            }
            throw new InvalidOperationException("Unexpected authority request.");
        }
    }
}
