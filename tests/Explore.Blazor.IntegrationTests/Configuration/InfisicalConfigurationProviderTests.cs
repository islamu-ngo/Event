using System.Collections.Concurrent;
using System.Security.Cryptography;
using Explore.Blazor.Configuration;
using Explore.ServiceDefaults.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.IntegrationTests.Configuration;

[NotInParallel]
public sealed class InfisicalConfigurationProviderTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcquiredOidcSettingsSurviveStartupProjection(bool hasSessionCredential)
    {
        await using var host = new BffSecretPreparationTestHost();
        var roots = CompleteRoots(host);
        string sessionPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        roots["/runtime/bff/session"] = hasSessionCredential
            ? [new("BFF_SESSION_REDIS_PASSWORD", sessionPassword, "/runtime/bff/session")] : [];
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bff:SessionAuthority:Password"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        });
        await using var authority = await NativeAuthority.StartAsync(path => roots.GetValueOrDefault(path) ?? []);
        await host.PrepareInfisicalAsync(authority.Url);

        var expected = new Dictionary<string, string?>
        {
            ["Keycloak:Realm"] = "operators",
            ["Keycloak:ClientId"] = "replacement-keycloak-client",
            ["Keycloak:Authority"] = "https://identity.example.test/realms/operators",
            ["Keycloak:ClientSecret"] = roots["/runtime/bff/oidc/keycloak"][3].secretValue,
            ["Google:ClientId"] = "replacement-google-client",
            ["Google:ClientSecret"] = roots["/runtime/bff/oidc/google"][1].secretValue,
            ["Bff:SessionAuthority:Password"] = hasSessionCredential ? sessionPassword : null
        };
        foreach (var setting in expected)
            await Assert.That(host.Builder.Configuration[setting.Key] == setting.Value).IsTrue()
                .Because($"Acquired {setting.Key} must survive application startup projection.");
        var acquired = host.Receipts.Snapshot();
        await Assert.That(acquired.Count).IsEqualTo(2);
        await Assert.That(acquired.All(receipt => receipt.ProcessBootId == host.Receipts.ProcessBootId
            && receipt.Authority == AuthenticationCredentialAcquisitionReceipts.Authority.Infisical
            && receipt.AuthorityProfileRevision == host.ProfileRevision
            && !receipt.Installed && receipt.Generation != Guid.Empty)).IsTrue();
        string serialized = System.Text.Json.JsonSerializer.Serialize(acquired);
        await Assert.That(serialized.Contains(expected["Keycloak:ClientSecret"]!, StringComparison.Ordinal)
            || serialized.Contains(expected["Google:ClientSecret"]!, StringComparison.Ordinal)
            || serialized.Contains(sessionPassword, StringComparison.Ordinal)
            || serialized.Contains(authority.AccessToken, StringComparison.Ordinal)).IsFalse();
        await Assert.That(host.LoginAttempt.Receipt).IsNotNull();
        await Assert.That(authority.Requests.Count(request => request.AbsolutePath ==
            "/api/v1/auth/universal-auth/login")).IsEqualTo(1);
        await Assert.That(host.Builder.Configuration["KEYCLOAK_BLAZOR_CLIENT_SECRET"]).IsNull();
        await Assert.That(host.Builder.Configuration["GOOGLE_CLIENT_SECRET"]).IsNull();
        await Assert.That(host.Builder.Configuration["BFF_SESSION_REDIS_PASSWORD"]).IsNull();
        await Assert.That(host.Builder.Services.Single(descriptor =>
            descriptor.ServiceType == typeof(SecretAuthorityAcquisition)).ImplementationInstance)
            .IsSameReferenceAs(host.Owner);
        await using var services = host.Builder.Services.BuildServiceProvider();
        var protector = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(Guid.NewGuid().ToString("N"));
        byte[] plaintext = RandomNumberGenerator.GetBytes(32);
        await Assert.That(protector.Unprotect(protector.Protect(plaintext))).IsEquivalentTo(plaintext);
    }

    [Test]
    [Arguments("/keycloak")]
    [Arguments("/blazor")]
    [Arguments("/atproto")]
    [Arguments("/runtime/bff/oidc/keycloak/nested")]
    [Arguments("/runtime/bff/oidc/google/nested")]
    [Arguments("/database")]
    [Arguments("/api")]
    [Arguments("/")]
    [Arguments("/runtime/bff/session/nested")]
    [Arguments("/runtime/api/session")]
    public async Task NonOidcRootIsRejectedBeforeAuthorityAuthentication(string path)
    {
        await using var host = new BffSecretPreparationTestHost();
        await using var authority = await NativeAuthority.StartAsync(_ => []);

        Func<Task> prepare = () => host.PrepareInfisicalAsync(authority.Url, path);

        var failure = await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("secret_consumer_scope_invalid");
        await Assert.That(authority.Requests).IsEmpty();
        await Assert.That(host.LoginAttempt.Receipt).IsNull();
        await host.AssertNothingPublished();
    }

    [Test]
    public async Task OidcAcquisitionUsesOnlyExactRootsWithoutReferenceExpansion()
    {
        await using var host = new BffSecretPreparationTestHost();
        var roots = CompleteRoots(host);
        await using var authority = await NativeAuthority.StartAsync(path => roots.GetValueOrDefault(path) ?? []);

        await host.PrepareInfisicalAsync(authority.Url);

        Uri[] acquisitions = authority.Requests.Where(request =>
            request.AbsolutePath.StartsWith("/api/v4/secrets/", StringComparison.Ordinal)).ToArray();
        var expectedPurposes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KEYCLOAK_ENDPOINT"] = "/runtime/bff/oidc/keycloak",
            ["KEYCLOAK_REALM"] = "/runtime/bff/oidc/keycloak",
            ["KEYCLOAK_CLIENT_ID"] = "/runtime/bff/oidc/keycloak",
            ["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = "/runtime/bff/oidc/keycloak",
            ["GOOGLE_CLIENT_ID"] = "/runtime/bff/oidc/google",
            ["GOOGLE_CLIENT_SECRET"] = "/runtime/bff/oidc/google",
            ["ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS"] = "/runtime/bff/atproto/oauth",
            ["BFF_SESSION_REDIS_PASSWORD"] = "/runtime/bff/session",
            ["EVENT_DP_BFF_RING"] = "/runtime/bff/data-protection",
            ["EVENT_DP_BFF_ADMISSION"] = "/admission/bff/data-protection"
        };
        await Assert.That(acquisitions.Length).IsEqualTo(expectedPurposes.Count);
        await Assert.That(acquisitions.Select(uri => uri.Segments.Last()).Distinct().Count())
            .IsEqualTo(expectedPurposes.Count);
        foreach (Uri acquisition in acquisitions)
        {
            var query = QueryHelpers.ParseQuery(acquisition.Query);
            await Assert.That(query["secretPath"].ToString())
                .IsEqualTo(expectedPurposes[acquisition.Segments.Last()]);
            await Assert.That(query["expandSecretReferences"].ToString()).IsEqualTo("false");
            await Assert.That(query.TryGetValue("recursive", out var recursive)
                && recursive.ToString() == "true").IsFalse();
            await Assert.That(query.TryGetValue("include_imports", out var imports)
                && imports.ToString() == "true").IsFalse();
        }
        await Assert.That(authority.Requests.Any(request => request.AbsolutePath == "/api/v4/secrets")).IsFalse();
        await Assert.That(authority.Requests.Any(request => request.AbsolutePath == "/api/v3/secrets/raw")).IsFalse();
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
        string material = value == "generated" ? Guid.NewGuid().ToString("N") : value;
        await using var host = new BffSecretPreparationTestHost();
        var roots = CompleteRoots(host);
        roots["/runtime/bff/oidc/google"] =
        [
            new("GOOGLE_CLIENT_ID", "replacement-google-client", "/runtime/bff/oidc/google"),
            new(key, material, returnedPath)
        ];
        await using var authority = await NativeAuthority.StartAsync(path => roots.GetValueOrDefault(path) ?? []);

        Func<Task> prepare = () => host.PrepareInfisicalAsync(authority.Url);

        var failure = await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("bff_secret_authority_unavailable");
        await Assert.That(failure.Message).DoesNotContain(material);
        await Assert.That(host.LoginAttempt.Receipt).IsNotNull();
        await host.AssertNothingPublished();
    }

    [Test]
    public async Task DuplicateAcquiredCredentialCannotBePartiallyPublished()
    {
        await using var host = new BffSecretPreparationTestHost();
        var roots = CompleteRoots(host);
        await using var authority = await NativeAuthority.StartAsync(
            path => roots.GetValueOrDefault(path) ?? [], duplicateCredential: true);

        Func<Task> prepare = () => host.PrepareInfisicalAsync(authority.Url);

        await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(authority.Requests.Any(request =>
            request.AbsolutePath == "/api/v4/secrets/GOOGLE_CLIENT_SECRET")).IsTrue();
        await host.AssertNothingPublished();
    }

    [Test]
    public async Task DedicatedAtprotoAcquisitionPreservesOnlyFrontendSigningKey()
    {
        await using var host = new BffSecretPreparationTestHost();
        var roots = DataProtectionRoots(host);
        string canary = Guid.NewGuid().ToString("N");
        roots["/runtime/bff/atproto/oauth"] =
            [new("ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS", canary, "/runtime/bff/atproto/oauth")];
        await using var authority = await NativeAuthority.StartAsync(path => roots.GetValueOrDefault(path) ?? []);

        await host.PrepareInfisicalAsync(authority.Url);

        await Assert.That(host.Builder.Configuration["Atproto:OauthClientPrivateJwks"]).IsEqualTo(canary);
        await Assert.That(host.Builder.Configuration["ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS"]).IsNull();
        await Assert.That(host.Receipts.Snapshot()).IsEmpty();
        Uri acquisition = authority.Requests.Single(request =>
            request.AbsolutePath == "/api/v4/secrets/ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS");
        var query = QueryHelpers.ParseQuery(acquisition.Query);
        await Assert.That(query["secretPath"].ToString()).IsEqualTo("/runtime/bff/atproto/oauth");
        await Assert.That(query["expandSecretReferences"].ToString()).IsEqualTo("false");
        await Assert.That(query.TryGetValue("recursive", out var recursive)
            && recursive.ToString() == "true").IsFalse();
    }

    [Test]
    public async Task BackendInventoryAndProviderSettingsCannotBeImportedThroughFrontendPurposes()
    {
        await using var host = new BffSecretPreparationTestHost();
        var roots = CompleteRoots(host);
        roots["/runtime/bff/oidc/google"] =
        [
            .. roots["/runtime/bff/oidc/google"],
            new("DATABASE_RUNTIME_PASSWORD", Guid.NewGuid().ToString("N"), "/runtime/bff/oidc/google"),
            new("JWT_SIGNING_KEY", Guid.NewGuid().ToString("N"), "/runtime/bff/oidc/google"),
            new("SECRET_PROVIDER", "Environment", "/runtime/bff/oidc/google"),
            new("API_ENDPOINT", "https://unselected.example.test", "/runtime/bff/oidc/google"),
            new("AUTHENTICATION_PROVIDER", "Keycloak", "/runtime/bff/oidc/google")
        ];
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["API_ENDPOINT"] = "https://api.example.test",
            ["Authentication:Provider"] = "Google"
        });
        await using var authority = await NativeAuthority.StartAsync(path => roots.GetValueOrDefault(path) ?? []);

        await host.PrepareInfisicalAsync(authority.Url);

        await Assert.That(host.Builder.Configuration["DATABASE_RUNTIME_PASSWORD"]).IsNull();
        await Assert.That(host.Builder.Configuration["JWT_SIGNING_KEY"]).IsNull();
        await Assert.That(host.Builder.Configuration["ExploreApi:BaseUrl"]).IsEqualTo("https://api.example.test");
        await Assert.That(host.Builder.Configuration["Authentication:Provider"]).IsEqualTo("Google");
        await Assert.That(host.Builder.Configuration["SecretProvider:Provider"]).IsEqualTo("Infisical");
        await Assert.That(authority.Requests.Any(request => request.AbsolutePath == "/api/v4/secrets")).IsFalse();
        await Assert.That(authority.Requests.Any(request => request.AbsolutePath.EndsWith(
            "/DATABASE_RUNTIME_PASSWORD", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    [Arguments("/runtime/bff/session", "BFF_SESSION_REDIS_PASSWORD", true)]
    [Arguments("/runtime/bff/session/nested", "BFF_SESSION_REDIS_PASSWORD", false)]
    [Arguments("/runtime/bff/session", "GOOGLE_CLIENT_SECRET", false)]
    [Arguments("/runtime/bff/session", "DATABASE_PASSWORD", false)]
    public async Task SessionCredentialRequiresExactRootAndKey(string returnedPath, string key, bool admitted)
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var host = new BffSecretPreparationTestHost();
        var roots = CompleteRoots(host);
        roots["/runtime/bff/session"] = [new(key, canary, returnedPath)];
        await using var authority = await NativeAuthority.StartAsync(path => roots.GetValueOrDefault(path) ?? []);
        InvalidOperationException? rejection = null;
        try { await host.PrepareInfisicalAsync(authority.Url); }
        catch (InvalidOperationException exception) { rejection = exception; }

        await Assert.That(rejection is null).IsEqualTo(admitted);
        string? value = host.Builder.Configuration["Bff:SessionAuthority:Password"];
        await Assert.That(value == (admitted ? canary : null)).IsTrue();
        await Assert.That(host.Builder.Configuration["BFF_SESSION_REDIS_PASSWORD"]).IsNull();
        Uri acquisition = authority.Requests.Single(request =>
            request.AbsolutePath == "/api/v4/secrets/BFF_SESSION_REDIS_PASSWORD");
        var query = QueryHelpers.ParseQuery(acquisition.Query);
        await Assert.That(query["secretPath"].ToString()).IsEqualTo("/runtime/bff/session");
        await Assert.That(query.TryGetValue("recursive", out var recursive)
            && recursive.ToString() == "true").IsFalse();
        await Assert.That(query["expandSecretReferences"].ToString()).IsEqualTo("false");
        await Assert.That(query.TryGetValue("include_imports", out var imports)
            && imports.ToString() == "true").IsFalse();
        await Assert.That(authority.Requests.Any(request =>
            request.AbsolutePath is "/api/v4/secrets" or "/api/v3/secrets/raw")).IsFalse();
        if (rejection is not null)
        {
            await Assert.That(rejection.InnerException).IsNull();
            await Assert.That(rejection.Message.Contains(canary, StringComparison.Ordinal)).IsFalse();
            await host.AssertNothingPublished();
        }
    }

    [Test]
    public async Task SelectedAuthorityRejectionStopsStartup()
    {
        await using var host = new BffSecretPreparationTestHost();
        await using var authority = await NativeAuthority.StartAsync(_ => [], rejectAuthentication: true);

        Func<Task> prepare = () => host.PrepareInfisicalAsync(authority.Url);

        var failure = await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("secret_authority_unavailable");
        await Assert.That(failure.Message).DoesNotContain(authority.AccessToken);
        await Assert.That(authority.Requests.Count).IsEqualTo(1);
        await Assert.That(host.LoginAttempt.Receipt).IsNotNull();
        await host.AssertNothingPublished();
    }

    private static Dictionary<string, RawSecret[]> CompleteRoots(BffSecretPreparationTestHost host)
    {
        var roots = DataProtectionRoots(host);
        const string keycloak = "/runtime/bff/oidc/keycloak";
        const string google = "/runtime/bff/oidc/google";
        roots[keycloak] =
        [
            new("KEYCLOAK_ENDPOINT", "https://identity.example.test", keycloak),
            new("KEYCLOAK_REALM", "operators", keycloak),
            new("KEYCLOAK_CLIENT_ID", "replacement-keycloak-client", keycloak),
            new("KEYCLOAK_BLAZOR_CLIENT_SECRET", Guid.NewGuid().ToString("N"), keycloak)
        ];
        roots[google] =
        [
            new("GOOGLE_CLIENT_ID", "replacement-google-client", google),
            new("GOOGLE_CLIENT_SECRET", Guid.NewGuid().ToString("N"), google)
        ];
        return roots;
    }

    private static Dictionary<string, RawSecret[]> DataProtectionRoots(BffSecretPreparationTestHost host)
    {
        var values = host.DataProtectionValues;
        return new()
        {
            ["/runtime/bff/data-protection"] =
                [new("EVENT_DP_BFF_RING", values["EVENT_DP_BFF_RING"]!, "/runtime/bff/data-protection")],
            ["/admission/bff/data-protection"] =
                [new("EVENT_DP_BFF_ADMISSION", values["EVENT_DP_BFF_ADMISSION"]!, "/admission/bff/data-protection")]
        };
    }

    private sealed record RawSecret(string secretKey, string secretValue, string? secretPath);

    /// <summary>Exercises the public native HTTP boundary; it contains no SDK implementation.</summary>
    private sealed class NativeAuthority(WebApplication app) : IAsyncDisposable
    {
        internal string Url => app.Urls.Single();
        internal string AccessToken { get; } = Guid.NewGuid().ToString("N");
        internal ConcurrentQueue<Uri> Requests { get; } = new();

        internal static async Task<NativeAuthority> StartAsync(
            Func<string, RawSecret[]> response, bool rejectAuthentication = false, bool duplicateCredential = false)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var authority = new NativeAuthority(app);
            var identities = new ConcurrentDictionary<(string? Path, string Key), string>();
            app.Use(async (context, next) =>
            {
                authority.Requests.Enqueue(new Uri($"{context.Request.Scheme}://{context.Request.Host}"
                    + $"{context.Request.Path}{context.Request.QueryString}"));
                await next(context);
            });
            app.MapPost("/api/v1/auth/universal-auth/login", () => rejectAuthentication
                ? Results.Json(new { message = authority.AccessToken }, statusCode: StatusCodes.Status401Unauthorized)
                : Results.Json(new
                {
                    accessToken = authority.AccessToken, accessTokenMaxTTL = 3600, expiresIn = 600, tokenType = "Bearer"
                }));
            object Observation(RawSecret secret, HttpContext context, bool hidden) => new
            {
                id = identities.GetOrAdd((secret.secretPath, secret.secretKey), _ => Guid.CreateVersion7().ToString("N")),
                workspace = context.Request.Query["projectId"].ToString(),
                environment = context.Request.Query["environment"].ToString(),
                secret.secretPath, secret.secretKey,
                secretValue = hidden ? null : secret.secretValue,
                secretValueHidden = hidden, type = "shared", version = 1
            };
            app.MapGet("/api/v4/secrets", (HttpContext context) =>
            {
                string path = context.Request.Query["secretPath"].ToString();
                return Results.Json(new { secrets = response(path).Select(secret => Observation(secret, context, true)) });
            });
            app.MapGet("/api/v4/secrets/{key}", (HttpContext context) =>
            {
                string path = context.Request.Query["secretPath"].ToString();
                string key = (string)context.Request.RouteValues["key"]!;
                RawSecret[] available = response(path);
                // Do not repair hostile path/key observations before native validation sees them.
                RawSecret? secret = available.FirstOrDefault(secret => secret.secretKey == key) ?? available.LastOrDefault();
                if (secret is null)
                    return Results.NotFound();
                if (duplicateCredential && key == "GOOGLE_CLIENT_SECRET")
                {
                    object observation = Observation(secret, context, false);
                    return Results.Json(new { secret = new[] { observation, observation } });
                }
                return Results.Json(new { secret = Observation(secret, context, false) });
            });
            try
            {
                await app.StartAsync().WaitAsync(TimeSpan.FromSeconds(15));
                return authority;
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await app.DisposeAsync();
        }
    }
}
