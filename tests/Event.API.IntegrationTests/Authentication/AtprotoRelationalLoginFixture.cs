// ABOUTME: Hosts real PostgreSQL/API authentication and independent Production BFF replicas without Redis.
// ABOUTME: Substitutes only external ATProto transports and secret authority while sharing persistent Data Protection keys.

extern alias bff;

using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CarpaNet.Identity;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Atproto.Transport;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Services.Federation;
using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using TUnit.Core.Interfaces;
using BffAuth = bff::Explore.Blazor.Services.Auth;

namespace Event.API.IntegrationTests.Authentication;

public sealed class AtprotoRelationalLoginFixture : IAsyncInitializer, IAsyncDisposable
{
    public const string CanonicalOrigin = "https://events.example.com";
    public const string TenantOrigin = "https://independent-community.example.net";
    private readonly AtprotoTransientApiFixture database = new();
    private readonly string keyDirectory = Path.Combine(Path.GetTempPath(), "atproto-relational-login-" + Guid.CreateVersion7().ToString("N"));
    private string oauthClientRing = string.Empty;
    public PostgreSqlApiWebApplicationFactory Api { get; private set; } = null!;
    public Guid TenantId { get; private set; }
    public bool ConfiguredOnboarding { get; init; }
    public bool DisableRateLimiting { get; init; } = true;
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public string TenantSlug => "transient-" + TenantId.ToString("N");
    public ExternalAtprotoTransport External { get; } = new();

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        TenantId = await database.SeedTenantAsync();
        string connection;
        await using (var scope = database.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            connection = db.Database.GetConnectionString()!;
            if (!ConfiguredOnboarding)
            {
                Guid administratorId = Guid.CreateVersion7();
                var now = DateTime.UtcNow;
                db.Users.Add(new User
                {
                    Id = administratorId,
                    Pii = new UserPii { Email = $"fixture-{administratorId:N}@example.test", FirstName = "Fixture", LastName = "Operator" },
                    CreatedAt = now
                });
                var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.SingleTenant, now);
                bootstrap.CompleteInteractive(administratorId, now);
                db.InstanceBootstrapStates.Add(bootstrap);
                await db.SaveChangesAsync();
            }
        }

        oauthClientRing = (await database.Secrets.ResolveAsync(
            SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks, null)).Value!;
        var material = new Dictionary<string, string>
        {
            [SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks] = oauthClientRing,
            [SecretDefinitionRegistry.Keys.Atproto.SessionJwtPrivateJwks] = CreateSigningRing(),
            [SecretDefinitionRegistry.Keys.Atproto.SessionEncryptionKeyRing] = JsonSerializer.Serialize(new
            {
                keys = new[] { new { kid = "session-envelope", status = "active", k = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)) } }
            })
        };
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            string name = call.ArgAt<string>(0);
            return material.TryGetValue(name, out string? value)
                ? SecretResolutionResult.Resolved(new ResolvedSecret(name, value, SecretSourceType.Infisical,
                    SecretScope.Instance, null, DateTimeOffset.UtcNow))
                : await database.Secrets.ResolveAsync(name, call.ArgAt<Guid?>(1), call.ArgAt<CancellationToken>(2));
        });
        var apiConfiguration = new Dictionary<string, string?>
        {
            ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
            ["RateLimiting:DisableInTesting"] = DisableRateLimiting.ToString(),
            ["Deployment:Mode"] = "SingleTenant",
            ["Deployment:DefaultTenantId"] = TenantId.ToString("D"),
            ["Authentication:Provider"] = "atproto",
            ["Authorization:Provider"] = "local",
            ["Atproto:PublicUrl"] = CanonicalOrigin,
            ["Atproto:CallbackPath"] = "/signin-atproto"
        };
        if (ConfiguredOnboarding)
        {
            apiConfiguration["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator";
            apiConfiguration["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "atproto";
            apiConfiguration["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = External.SubjectDid;
            apiConfiguration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1";
            apiConfiguration["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = "configured-admin@example.test";
            apiConfiguration["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"] = "Fixture";
            apiConfiguration["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"] = "Administrator";
            apiConfiguration["Instance:OperatorIdentity:OperatorId"] = Guid.CreateVersion7().ToString("D");
            apiConfiguration["Instance:OperatorIdentity:PublicName"] = "Fixture Operator";
            apiConfiguration["Instance:OperatorIdentity:LegalName"] = "Fixture Operator ASBL";
            apiConfiguration["Instance:OperatorIdentity:IsOfficialInstance"] = "false";
            apiConfiguration["Instance:OperatorIdentity:OfficialOrigin"] = CanonicalOrigin;
            apiConfiguration["Instance:OperatorIdentity:OperatorKindCode"] = "registered_organization";
            apiConfiguration["Instance:OperatorIdentity:JurisdictionCountryCode"] = "BE";
            apiConfiguration["Instance:OperatorIdentity:PublicContactEmail"] = "operator@example.test";
            apiConfiguration["Instance:OperatorIdentity:WebsiteUrl"] = CanonicalOrigin;
            apiConfiguration["Instance:OperatorIdentity:LegalNoticeUrl"] = CanonicalOrigin + "/legal";
            apiConfiguration["Instance:OperatorIdentity:TermsUrl"] = CanonicalOrigin + "/terms";
            apiConfiguration["Instance:OperatorIdentity:PrivacyUrl"] = CanonicalOrigin + "/privacy";
        }
        Api = new PostgreSqlApiWebApplicationFactory(connection, apiConfiguration, services =>
        {
            database.ConfigureServices(services);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(Clock);
            services.RemoveAll<ISecretResolver>();
            services.AddSingleton(secrets);
            services.RemoveAll<IAuthorizationProvider>();
            services.AddScoped<IAuthorizationProvider>(provider => provider.GetRequiredService<RuntimeAuthorizationProvider>());
            services.RemoveAll<AtprotoOAuthClientFactory>();
            services.AddScoped(provider => new AtprotoOAuthClientFactory(secrets,
                provider.GetRequiredService<IOptions<AtprotoInfrastructureOptions>>(),
                provider.GetRequiredService<IHostEnvironment>(), _ => External.CreateHandler()));
            services.RemoveAll<AtprotoCoreClientFactory>();
            services.AddScoped(provider => new AtprotoCoreClientFactory(
                provider.GetRequiredService<AtprotoOAuthClientFactory>(), External));
        });
        _ = Api.Server;
        if (ConfiguredOnboarding)
        {
            await using var scope = Api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ConfiguredAdministratorBootstrapStartupRunner>().PrepareAsync();
        }
        Directory.CreateDirectory(keyDirectory);
    }

    public WebApplicationFactory<bff::Program> CreateBff(bool rotateKeys = false) => new WebApplicationFactory<bff::Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Provider"] = "Environment",
                ["ExploreApi:BaseUrl"] = "https://api.test",
                ["ConnectionStrings:cache"] = string.Empty,
                ["Authentication:Provider"] = "atproto",
                ["Authentication:AtprotoLoginEnabled"] = "true",
                ["Atproto:PublicUrl"] = CanonicalOrigin,
                ["Atproto:CallbackPath"] = "/signin-atproto",
                ["Atproto:TenantOrigins:0:Origin"] = TenantOrigin,
                ["Atproto:TenantOrigins:0:TenantId"] = TenantId.ToString("D"),
                ["Atproto:TenantOrigins:0:TenantSlug"] = TenantSlug,
                ["Explore:MultiTenancy:DefaultTenantId"] = TenantId.ToString("D"),
                ["Explore:MultiTenancy:DefaultTenant"] = TenantSlug,
                ["Deployment:Mode"] = "SingleTenant",
                ["Deployment:DefaultTenantId"] = TenantId.ToString("D")
            }));
            builder.UseSetting("SecretProvider:Provider", "Environment");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(Clock);
                services.PostConfigure<BffAuth.AtprotoClientKeyOptions>(options =>
                {
                    var ring = JsonNode.Parse(oauthClientRing)!;
                    if (rotateKeys)
                        foreach (var key in ring["keys"]!.AsArray())
                            key!["status"] = key["status"]!.GetValue<string>() == "active" ? "retired" : "active";
                    options.OAuthClientPrivateJwks = ring.ToJsonString();
                });
                services.RemoveAll<BffAuth.IAtprotoOAuthTransportFactory>();
                services.AddSingleton<BffAuth.IAtprotoOAuthTransportFactory>(External);
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(_ => CreateDataProtectionProvider());
                // Preserve all registered client/auth/resilience handlers; replace only the network hop to the real API host.
                services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(
                    handlerBuilder => handlerBuilder.PrimaryHandler = Api.Server.CreateHandler()));
            });
        });

    public async ValueTask DisposeAsync()
    {
        if (Api is not null) await Api.DisposeAsync();
        await database.DisposeAsync();
        if (Directory.Exists(keyDirectory)) Directory.Delete(keyDirectory, recursive: true);
    }

    public IDataProtectionProvider CreateDataProtectionProvider() => DataProtectionProvider.Create(
        new DirectoryInfo(keyDirectory), configuration => configuration.SetApplicationName(bff::Explore.Blazor.Extensions.BffDataProtectionExtensions.ApplicationName));

    public static HttpClient BrowserClient<TEntryPoint>(WebApplicationFactory<TEntryPoint> factory, string origin, CookieContainer cookies)
        where TEntryPoint : class =>
        new(new BrowserCookies(cookies) { InnerHandler = factory.Server.CreateHandler() }) { BaseAddress = new(origin) };

    private sealed class BrowserCookies(CookieContainer cookies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string header = cookies.GetCookieHeader(request.RequestUri!);
            if (header.Length > 0) request.Headers.Add("Cookie", header);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (string value in values) cookies.SetCookies(request.RequestUri!, value);
            return response;
        }
    }

    private static string CreateSigningRing()
    {
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = signing.ExportParameters(true);
        return JsonSerializer.Serialize(new { keys = new[] { new
        {
            kty = "EC", crv = "P-256", kid = "platform-session", use = "sig", alg = "ES256", status = "active",
            x = Base64UrlEncoder.Encode(key.Q.X!), y = Base64UrlEncoder.Encode(key.Q.Y!), d = Base64UrlEncoder.Encode(key.D!)
        } } });
    }

    public sealed class ExternalAtprotoTransport : BffAuth.IAtprotoOAuthTransportFactory, IAtprotoCorePrimaryHandlerFactory
    {
        public const string Did = "did:plc:abcdefghijklmnopqrstuvwx";
        public string AuthorizationCode { get; private set; } = string.Empty;
        public string AccessToken { get; } = RandomValue();
        public string RefreshToken { get; } = RandomValue();
        public string? State { get; private set; }
        public string? TokenClientKeyId { get; private set; }
        private readonly ConcurrentDictionary<string, (string State, string Code, string Challenge)> authorizations = new();
        private int dnsRequests, didDocumentRequests, authorizationMetadataRequests, pushedAuthorizationRequests;
        public int DnsRequests => Volatile.Read(ref dnsRequests);
        public int DidDocumentRequests => Volatile.Read(ref didDocumentRequests);
        public int AuthorizationMetadataRequests => Volatile.Read(ref authorizationMetadataRequests);
        public int PushedAuthorizationRequests => Volatile.Read(ref pushedAuthorizationRequests);
        public bool UseDidWeb { get; set; }
        public string AuthorizationEndpoint { get; set; } = "https://issuer.example/oauth/authorize";
        public string? IdentityDocumentScenario { get; set; }
        public string SubjectDid => UseDidWeb ? "did:web:alice.example" : Did;
        public Func<CancellationToken, Task>? BeforeParResponse { get; set; }
        public int VerifiedPdsRequests { get; private set; }
        public HttpMessageHandler CreateHandler() => new ExternalHandler(this);
        public HttpMessageHandler CreatePrimaryHandler(AtprotoOutboundPolicy policy, TimeSpan connectTimeout) => CreateHandler();
        public HttpMessageHandler CreateOAuthPrimary(AtprotoOutboundPolicy policy) => CreateHandler();
        public HttpMessageHandler CreatePdsPrimary(AtprotoOutboundPolicy policy) => CreateHandler();
        public IDnsResolver CreateDnsResolver() => new ExternalDns(this);

        public (string State, string Code) ResolveAuthorization(string authorizationUrl)
        {
            string requestUri = QueryHelpers.ParseQuery(new Uri(authorizationUrl).Query)["request_uri"].ToString();
            var authorization = authorizations[requestUri];
            return (authorization.State, authorization.Code);
        }

        private sealed class ExternalDns(ExternalAtprotoTransport owner) : IDnsResolver
        {
            public Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref owner.dnsRequests);
                return Task.FromResult<IReadOnlyList<string>>(name == "_atproto.alice.example" ? ["did=" + owner.SubjectDid] : []);
            }
        }

        private sealed class ExternalHandler(ExternalAtprotoTransport owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri!;
                if (request.Method == HttpMethod.Get && (uri.Host == "plc.directory" && uri.AbsolutePath == "/" + Did
                    || owner.UseDidWeb && uri.Host == "alice.example" && uri.AbsolutePath == "/.well-known/did.json"))
                {
                    Interlocked.Increment(ref owner.didDocumentRequests);
                    object Service(string endpoint) => new { id = "#atproto_pds", type = "AtprotoPersonalDataServer", serviceEndpoint = endpoint };
                    object[] services = owner.IdentityDocumentScenario switch
                    {
                        "missing_pds" => [],
                        "duplicate_pds" => [Service("https://pds.example"), Service("https://other-pds.example")],
                        "non_https_pds" => [Service("http://pds.example")],
                        "invalid_pds" => [Service("not-a-uri")],
                        _ => [Service("https://pds.example")]
                    };
                    return Json(new { id = owner.SubjectDid,
                        alsoKnownAs = new[] { owner.IdentityDocumentScenario == "conflicting_handle" ? "at://mallory.example" : "at://alice.example" },
                        service = services });
                }
                if (request.Method == HttpMethod.Get && uri.Host == "pds.example" && uri.AbsolutePath == "/.well-known/oauth-protected-resource")
                    return Json(new { authorization_servers = new[] { "https://issuer.example" } });
                if (request.Method == HttpMethod.Get && uri.Host == "issuer.example" && uri.AbsolutePath == "/.well-known/oauth-authorization-server")
                {
                    Interlocked.Increment(ref owner.authorizationMetadataRequests);
                    return Json(new
                    {
                        issuer = "https://issuer.example", authorization_endpoint = owner.AuthorizationEndpoint,
                        token_endpoint = "https://issuer.example/oauth/token", pushed_authorization_request_endpoint = "https://issuer.example/oauth/par",
                        revocation_endpoint = "https://issuer.example/oauth/revoke", require_pushed_authorization_requests = true,
                        token_endpoint_auth_methods_supported = new[] { "private_key_jwt" }, token_endpoint_auth_signing_alg_values_supported = new[] { "ES256" },
                        dpop_signing_alg_values_supported = new[] { "ES256" }, grant_types_supported = new[] { "authorization_code", "refresh_token" },
                        response_types_supported = new[] { "code" }, code_challenge_methods_supported = new[] { "S256" },
                        authorization_response_iss_parameter_supported = true, client_id_metadata_document_supported = true,
                        scopes_supported = new[] { "atproto" }, require_request_uri_registration = true
                    });
                }
                if (request.Method == HttpMethod.Post && uri.Host == "issuer.example" && uri.AbsolutePath is "/oauth/par" or "/oauth/token")
                {
                    var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&').Select(part => part.Split('=', 2))
                        .ToDictionary(part => Uri.UnescapeDataString(part[0].Replace('+', ' ')), part => Uri.UnescapeDataString(part[1].Replace('+', ' ')));
                    if (!form.ContainsKey("client_assertion") || !request.Headers.Contains("DPoP"))
                        throw new InvalidOperationException("External authorization request must use private-key JWT and DPoP.");
                    if (uri.AbsolutePath == "/oauth/par")
                    {
                        string state = form["state"];
                        string code = RandomValue();
                        string requestUri = "urn:ietf:params:oauth:request_uri:" + RandomValue();
                        owner.authorizations[requestUri] = (state, code, form["code_challenge"]);
                        owner.State = state;
                        owner.AuthorizationCode = code;
                        Interlocked.Increment(ref owner.pushedAuthorizationRequests);
                        if (owner.BeforeParResponse is { } beforeResponse) await beforeResponse(cancellationToken);
                        return JsonWithNonce(new { request_uri = requestUri, expires_in = 90 });
                    }
                    var matched = owner.authorizations.FirstOrDefault(entry => entry.Value.Code == form["code"]);
                    if (matched.Key is null || !owner.authorizations.TryRemove(matched.Key, out var authorization)
                        || Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))) != authorization.Challenge)
                        throw new InvalidOperationException("External authorization code or PKCE verifier is invalid.");
                    owner.TokenClientKeyId = new JsonWebToken(form["client_assertion"]).Kid;
                    return JsonWithNonce(new { access_token = owner.AccessToken, token_type = "DPoP", expires_in = 3600,
                        refresh_token = owner.RefreshToken, scope = "atproto transition:generic", sub = owner.SubjectDid });
                }
                if (request.Method == HttpMethod.Get && uri.Host == "pds.example" && uri.AbsolutePath == "/xrpc/com.atproto.server.getSession")
                {
                    if (request.Headers.Authorization?.Scheme != "DPoP" || request.Headers.Authorization.Parameter != owner.AccessToken
                        || !request.Headers.Contains("DPoP")) throw new InvalidOperationException("PDS session verification must use the bound DPoP token.");
                    owner.VerifiedPdsRequests++;
                    return Json(new { did = owner.SubjectDid, handle = "alice.example", active = true });
                }
                throw new InvalidOperationException("Unexpected external ATProto request.");
            }

            private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

            private static HttpResponseMessage JsonWithNonce(object value)
            {
                var response = Json(value);
                response.Headers.Add("DPoP-Nonce", RandomValue());
                return response;
            }
        }

        private static string RandomValue() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    }
}
