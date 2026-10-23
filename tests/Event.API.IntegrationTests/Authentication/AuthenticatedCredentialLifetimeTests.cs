using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Extensions;
using Explore.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Event.Api.IntegrationTests.Authentication;

public sealed class AuthenticatedCredentialLifetimeTests
{
    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Registered_bearer_observes_signed_lifetime_and_account(bool legacy)
    {
        await using var request = new BearerRequest(legacy);
        AuthenticateResult result = await request.AuthenticateAsync();
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(request.Representation).IsEqualTo(legacy ? "JwtSecurityToken" : "JsonWebToken");

        HumanAuthenticationLifetime? lifetime = await request.ObserveAsync(request.Context.User);
        await Assert.That(lifetime).IsNotNull();
        await Assert.That(lifetime!.AuthenticationScheme).IsEqualTo(JwtBearerDefaults.AuthenticationScheme);
        await Assert.That(lifetime.AccountKey).IsEqualTo(request.Context.User.GetProviderIdentity()!.AccountKey);
        await Assert.That(lifetime.ExpiresAt).IsEqualTo(request.ExpiresAt);
        await Assert.That(lifetime.NotBefore).IsEqualTo((DateTimeOffset?)request.NotBefore);
        await Assert.That(lifetime.ClockSkew).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(lifetime.LocalSession).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Signed_bounds_are_inclusive_to_one_tick_at_each_skew_boundary(bool legacy)
    {
        await using var request = new BearerRequest(legacy);
        await Assert.That((await request.AuthenticateAsync()).Succeeded).IsTrue();
        HumanAuthenticationLifetime lifetime = (await request.ObserveAsync(request.Context.User))!;
        await Assert.That(lifetime).IsNotNull();

        DateTimeOffset first = request.NotBefore.AddMinutes(-5);
        DateTimeOffset last = request.ExpiresAt.AddMinutes(5);
        request.Clock.Now = first.AddTicks(-1);
        await Assert.That(await request.ObserveAsync(request.Context.User)).IsNull();
        await Assert.That(lifetime.IsValidAt(first.AddTicks(-1))).IsFalse();
        request.Clock.Now = first;
        await Assert.That(await request.ObserveAsync(request.Context.User)).IsSameReferenceAs(lifetime);
        await Assert.That(lifetime.IsValidAt(first)).IsTrue();
        request.Clock.Now = request.ExpiresAt;
        await Assert.That(await request.ObserveAsync(request.Context.User)).IsSameReferenceAs(lifetime);
        request.Clock.Now = last;
        await Assert.That(await request.ObserveAsync(request.Context.User)).IsSameReferenceAs(lifetime);
        await Assert.That(lifetime.IsValidAt(last)).IsTrue();
        request.Clock.Now = last.AddTicks(1);
        await Assert.That(await request.ObserveAsync(request.Context.User)).IsNull();
        await Assert.That(lifetime.IsValidAt(last.AddTicks(1))).IsFalse();
    }

    [Test]
    [Arguments(false, "claims")]
    [Arguments(true, "claims")]
    [Arguments(false, "ticket")]
    [Arguments(true, "ticket")]
    public async Task Mutable_metadata_cannot_extend_signed_expiry(bool legacy, string mutation)
    {
        await using var request = new BearerRequest(legacy);
        AuthenticateResult result = await request.AuthenticateAsync();
        await Assert.That(result.Succeeded).IsTrue();
        HumanAuthenticationLifetime lifetime = (await request.ObserveAsync(request.Context.User))!;
        await Assert.That(lifetime).IsNotNull();
        if (mutation == "claims")
        {
            var identity = (ClaimsIdentity)request.Context.User.Identity!;
            foreach (Claim claim in identity.FindAll("exp").ToArray())
                identity.RemoveClaim(claim);
            identity.AddClaim(new Claim("exp", request.ExpiresAt.AddDays(1).ToUnixTimeSeconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
        }
        else
        {
            result.Properties!.ExpiresUtc = request.ExpiresAt.AddDays(1);
            result.Properties.IssuedUtc = request.ExpiresAt;
        }

        await Assert.That((await request.ObserveAsync(request.Context.User))!.ExpiresAt).IsEqualTo(request.ExpiresAt);
        request.Clock.Now = request.ExpiresAt.AddMinutes(5).AddTicks(1);
        await Assert.That(await request.ObserveAsync(request.Context.User)).IsNull();
        await Assert.That(lifetime.IsValidAt(request.Clock.Now)).IsFalse();
    }

    [Test]
    [Arguments(false, "argument")]
    [Arguments(true, "argument")]
    [Arguments(false, "ambient")]
    [Arguments(true, "ambient")]
    public async Task Same_claims_replacement_cannot_inherit_identity_receipt(bool legacy, string replacement)
    {
        await using var request = new BearerRequest(legacy);
        await Assert.That((await request.AuthenticateAsync()).Succeeded).IsTrue();
        ClaimsPrincipal original = request.Context.User;
        await Assert.That(await request.ObserveAsync(original)).IsNotNull();
        var clone = new ClaimsPrincipal(original.Identities.Select(identity => identity.Clone()));
        if (replacement == "ambient")
            request.Context.User = clone;
        await Assert.That(await request.ObserveAsync(replacement == "argument" ? clone : original)).IsNull();
    }

    [Test]
    [Arguments(false, "principal")]
    [Arguments(true, "principal")]
    [Arguments(false, "failure")]
    [Arguments(true, "failure")]
    [Arguments(false, "scheme")]
    [Arguments(true, "scheme")]
    public async Task Final_authentication_cannot_inherit_an_earlier_validation_receipt(bool legacy, string finalization)
    {
        await using var request = new BearerRequest(legacy, finalization);
        AuthenticateResult result = await request.AuthenticateAsync();
        await Assert.That(request.Representation).IsEqualTo(legacy ? "JwtSecurityToken" : "JsonWebToken");
        await Assert.That(result.Succeeded).IsEqualTo(finalization != "failure");
        if (finalization == "scheme")
            await Assert.That(result.Ticket!.AuthenticationScheme).IsEqualTo(RelabeledBearerHandler.SchemeName);
        await Assert.That(await request.ObserveAsync(request.ValidatedPrincipal!)).IsNull();
        await Assert.That(await request.ObserveAsync(request.Context.User)).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Wrong_signature_never_produces_a_validation_receipt(bool legacy)
    {
        await using var request = new BearerRequest(legacy);
        request.Context.Request.Headers.Authorization = "Bearer " + request.Sign(request.Payload, wrongKey: true);
        await Assert.That((await request.AuthenticateAsync()).Succeeded).IsFalse();
        await Assert.That(request.Representation).IsNull();
        await Assert.That(await request.ObserveAsync(request.Context.User)).IsNull();
    }

    [Test]
    [Arguments(false, "missing-exp")]
    [Arguments(true, "missing-exp")]
    [Arguments(false, "string-exp")]
    [Arguments(true, "string-exp")]
    [Arguments(false, "fraction-exp")]
    [Arguments(true, "fraction-exp")]
    [Arguments(false, "array-exp")]
    [Arguments(true, "array-exp")]
    [Arguments(false, "overflow-exp")]
    [Arguments(true, "overflow-exp")]
    [Arguments(false, "duplicate-exp")]
    [Arguments(true, "duplicate-exp")]
    [Arguments(false, "conflicting-exp")]
    [Arguments(true, "conflicting-exp")]
    [Arguments(false, "string-nbf")]
    [Arguments(true, "string-nbf")]
    [Arguments(false, "fraction-nbf")]
    [Arguments(true, "fraction-nbf")]
    [Arguments(false, "null-nbf")]
    [Arguments(true, "null-nbf")]
    [Arguments(false, "overflow-nbf")]
    [Arguments(true, "overflow-nbf")]
    [Arguments(false, "duplicate-nbf")]
    [Arguments(true, "duplicate-nbf")]
    [Arguments(false, "conflicting-nbf")]
    [Arguments(true, "conflicting-nbf")]
    public async Task Malformed_or_duplicate_signed_bounds_produce_no_observation(bool legacy, string malformed)
    {
        await using var request = new BearerRequest(legacy);
        long expiry = request.ExpiresAt.ToUnixTimeSeconds();
        long start = request.NotBefore.ToUnixTimeSeconds();
        var payload = new Dictionary<string, object?>(request.Payload);
        string? duplicate = null;
        switch (malformed)
        {
            case "missing-exp": payload.Remove("exp"); break;
            case "string-exp": payload["exp"] = expiry.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
            case "fraction-exp": payload["exp"] = expiry + 0.5m; break;
            case "array-exp": payload["exp"] = new[] { expiry }; break;
            case "overflow-exp": payload["exp"] = long.MaxValue; break;
            case "duplicate-exp": duplicate = $"\"exp\":{expiry}"; break;
            case "conflicting-exp": duplicate = $"\"exp\":{expiry + 60}"; break;
            case "string-nbf": payload["nbf"] = start.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
            case "fraction-nbf": payload["nbf"] = start + 0.5m; break;
            case "null-nbf": payload["nbf"] = null; break;
            case "overflow-nbf": payload["nbf"] = long.MinValue; break;
            case "duplicate-nbf": duplicate = $"\"nbf\":{start}"; break;
            case "conflicting-nbf": duplicate = $"\"nbf\":{start - 60}"; break;
            default: throw new ArgumentOutOfRangeException(nameof(malformed));
        }
        string json = JsonSerializer.Serialize(payload);
        if (duplicate is not null)
            json = json[..^1] + "," + duplicate + "}";
        request.Context.Request.Headers.Authorization = "Bearer " + request.SignJson(json);

        AuthenticateResult result = await request.AuthenticateAsync();
        // Some native handlers reject malformed NumericDates before the event; others accept
        // a normalized value. Neither outcome may turn ambiguous raw bounds into a receipt.
        if (result.Succeeded)
            await Assert.That(request.Representation).IsEqualTo(legacy ? "JwtSecurityToken" : "JsonWebToken");
        else
            await Assert.That(result.Failure).IsNotNull();
        await Assert.That(await request.ObserveAsync(request.Context.User)).IsNull();
    }

    private sealed class ObservationClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Uses production registration and an independent RSA signer. Only discovery
    /// metadata is supplied locally; native signature, issuer, audience and lifetime validation run.
    /// The accessor is bound synchronously in the caller's execution context before authentication.
    /// </summary>
    private sealed class BearerRequest : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly IServiceScope scope;
        private readonly IHttpContextAccessor accessor;
        private readonly RSA authority = RSA.Create(2048);
        private readonly RSA untrustedAuthority = RSA.Create(2048);
        public KeycloakTokenContextFixture Authority { get; } = new();
        public ObservationClock Clock { get; } = new();
        public Dictionary<string, object?> Payload { get; }
        public DateTimeOffset ExpiresAt { get; }
        public DateTimeOffset NotBefore { get; }
        public DefaultHttpContext Context { get; }
        public string? Representation { get; private set; }
        public ClaimsPrincipal? ValidatedPrincipal { get; private set; }

        public BearerRequest(bool legacy, string? finalization = null)
        {
            Payload = Authority.ValidPayload();
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds((long)Payload["exp"]!);
            NotBefore = DateTimeOffset.FromUnixTimeSeconds((long)Payload["nbf"]!);
            Clock.Now = ExpiresAt.AddMinutes(-30);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = Authority.Issuer,
                ["Keycloak:RequireHttpsMetadata"] = "false",
                ["Authentication:Local:JwtKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            });
            builder.Services.AddSingleton<TimeProvider>(Clock);
            builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment, skipAuthorityWarmup: true);
            builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.UseSecurityTokenValidators = legacy;
                var metadata = new OpenIdConnectConfiguration { Issuer = Authority.Issuer };
                metadata.SigningKeys.Add(new RsaSecurityKey(authority.ExportParameters(false)) { KeyId = "lifetime-authority" });
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                var capture = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    await capture(context);
                    Representation = context.SecurityToken.GetType().Name;
                    ValidatedPrincipal = context.Principal;
                    if (finalization == "failure")
                        context.Fail("Rejected after native token validation.");
                    else if (finalization == "principal")
                        context.Principal = new ClaimsPrincipal(context.Principal!.Identities.Select(identity => identity.Clone()));
                };
            });
            if (finalization == "scheme")
            {
                builder.Services.AddAuthentication(options => options.DefaultAuthenticateScheme = RelabeledBearerHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, RelabeledBearerHandler>(RelabeledBearerHandler.SchemeName, _ => { });
            }
            application = builder.Build();
            scope = application.Services.CreateScope();
            Context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            Context.Request.Headers.Authorization = "Bearer " + Sign(Payload);
            accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = Context;
        }

        public async Task<AuthenticateResult> AuthenticateAsync()
        {
            AuthenticateResult result = await Context.AuthenticateAsync();
            if (result.Succeeded)
                Context.User = result.Principal!;
            return result;
        }

        public ValueTask<HumanAuthenticationLifetime?> ObserveAsync(ClaimsPrincipal principal) =>
            Context.RequestServices.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(principal, CancellationToken);

        public string Sign(Dictionary<string, object?> payload, bool wrongKey = false) =>
            SignJson(JsonSerializer.Serialize(payload), wrongKey);

        public string SignJson(string payload, bool wrongKey = false)
        {
            string header = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = "RS256",
                kid = "lifetime-authority",
                typ = "JWT"
            }));
            string input = $"{header}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload))}";
            byte[] signature = (wrongKey ? untrustedAuthority : authority).SignData(
                Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return $"{input}.{Base64UrlEncoder.Encode(signature)}";
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            scope.Dispose();
            await application.DisposeAsync();
            await Authority.DisposeAsync();
            authority.Dispose();
            untrustedAuthority.Dispose();
        }
    }

    /// <summary>Changes only the final ticket's scheme after the native bearer handler succeeds.</summary>
    public sealed class RelabeledBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "RelabeledBearer";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            AuthenticateResult bearer = await Context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            return bearer.Succeeded
                ? AuthenticateResult.Success(new AuthenticationTicket(bearer.Principal!, bearer.Properties, SchemeName))
                : bearer;
        }
    }
}
