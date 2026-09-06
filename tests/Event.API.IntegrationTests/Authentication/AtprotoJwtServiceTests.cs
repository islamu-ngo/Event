// ABOUTME: Exercises the ATProto bootstrap/session JWT attack matrix and bounded MultiAuth selector.
// ABOUTME: Proves purpose-separated ES256 validation, tenant binding, and Keycloak/API-key routing parity.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.Application.Configuration;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Event.API.IntegrationTests.Authentication;

public sealed class AtprotoJwtServiceTests
{
    [Test]
    public async Task BootstrapValidationRejectsConfusionExpiryBindingAndSizeAttackMatrix()
    {
        using var keys = new TestKeyMaterial();
        var service = CreateService(keys);
        var tenantId = Guid.NewGuid();
        var valid = CreateBootstrapToken(keys.OAuthKey, tenantId);

        await Assert.That(await service.ValidateBootstrapAsync(
            valid, tenantId, HttpMethods.Post, AtprotoJwtOptions.BridgePath, CancellationToken.None)).IsNotNull();

        var attacks = new[]
        {
            string.Empty,
            new string('a', AtprotoJwtOptions.MaximumBootstrapTokenBytes + 1),
            CreateBootstrapToken(keys.OAuthKey, tenantId, audience: "wrong-audience"),
            CreateBootstrapToken(keys.OAuthKey, Guid.NewGuid()),
            CreateBootstrapToken(keys.OAuthKey, tenantId, method: HttpMethods.Get),
            CreateBootstrapToken(keys.OAuthKey, tenantId, path: "/api/other"),
            CreateBootstrapToken(
                keys.OAuthKey,
                tenantId,
                notBefore: DateTime.UtcNow.AddMinutes(-3),
                expires: DateTime.UtcNow.AddMinutes(-2)),
            CreateBootstrapToken(
                keys.OAuthKey,
                tenantId,
                notBefore: DateTime.UtcNow.AddMinutes(2),
                expires: DateTime.UtcNow.AddMinutes(3)),
            CreateBootstrapToken(keys.OAuthKey, tenantId, includeIssuedAt: false),
            CreateBootstrapToken(keys.OAuthKey, tenantId, issuedAt: DateTimeOffset.UtcNow.AddMinutes(2)),
            CreateBootstrapToken(keys.OAuthKey, tenantId, classification: null),
            CreateBootstrapToken(keys.OAuthKey, tenantId, classification: "unknown"),
            CreateBootstrapToken(keys.UnknownKey, tenantId, keyId: "unknown"),
            CreateHs256Token(tenantId),
            CreateUnsignedToken(tenantId)
        };

        foreach (var attack in attacks)
        {
            await Assert.That(await service.ValidateBootstrapAsync(
                attack, tenantId, HttpMethods.Post, AtprotoJwtOptions.BridgePath, CancellationToken.None)).IsNull();
        }
    }

    [Test]
    public async Task BootstrapValidationRejectsDuplicateMalformedOrHalfCanonicalActorTargetClaims()
    {
        using var keys = new TestKeyMaterial();
        var service = CreateService(keys);
        var tenantId = Guid.NewGuid();
        var canonicalActorId = Guid.NewGuid();
        var valid = CreateBootstrapToken(
            keys.OAuthKey,
            tenantId,
            canonicalActorId: canonicalActorId,
            expectedCanonicalActorConcurrencyStamp: Guid.NewGuid());
        var duplicate = CreateBootstrapToken(
            keys.OAuthKey,
            tenantId,
            canonicalActorId: canonicalActorId,
            expectedCanonicalActorConcurrencyStamp: Guid.NewGuid(),
            extraClaims: [new Claim(AtprotoJwtOptions.CanonicalActorIdClaim, Guid.NewGuid().ToString("D"))]);
        var malformed = CreateBootstrapToken(
            keys.OAuthKey,
            tenantId,
            canonicalActorId: Guid.Empty,
            expectedCanonicalActorConcurrencyStamp: Guid.NewGuid());
        var malformedStamp = CreateBootstrapToken(
            keys.OAuthKey,
            tenantId,
            canonicalActorId: canonicalActorId,
            extraClaims: [new Claim(AtprotoJwtOptions.ExpectedCanonicalActorConcurrencyStampClaim, "not-a-guid")]);
        var emptyStamp = CreateBootstrapToken(
            keys.OAuthKey,
            tenantId,
            canonicalActorId: canonicalActorId,
            extraClaims: [new Claim(AtprotoJwtOptions.ExpectedCanonicalActorConcurrencyStampClaim, Guid.Empty.ToString("D"))]);
        var half = CreateBootstrapToken(keys.OAuthKey, tenantId, canonicalActorId: canonicalActorId);

        await Assert.That(await service.ValidateBootstrapAsync(valid, tenantId, HttpMethods.Post, AtprotoJwtOptions.BridgePath, CancellationToken.None)).IsNotNull();
        foreach (var attack in new[] { duplicate, malformed, malformedStamp, emptyStamp, half })
        {
            await Assert.That(await service.ValidateBootstrapAsync(attack, tenantId, HttpMethods.Post, AtprotoJwtOptions.BridgePath, CancellationToken.None)).IsNull();
        }
    }

    [Test]
    public async Task IssuedSessionUsesPlatformUserSubjectAndRequiresServerTenantAndKnownSessionKey()
    {
        using var keys = new TestKeyMaterial();
        var service = CreateService(keys);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var did = "did:plc:session-user";

        var issued = await service.IssueAsync(userId, tenantId, AtprotoDid.Parse(did), CancellationToken.None);
        var principal = await service.ValidateSessionAsync(issued.Token, tenantId, CancellationToken.None);

        await Assert.That(principal).IsNotNull();
        await Assert.That(principal!.FindFirstValue(JwtRegisteredClaimNames.Sub)).IsEqualTo(userId.ToString("D"));
        await Assert.That(principal.FindFirstValue(AtprotoJwtOptions.TenantClaim)).IsEqualTo(tenantId.ToString("D"));
        await Assert.That(principal.FindFirstValue(AtprotoJwtOptions.DidClaim)).IsEqualTo(did);
        await Assert.That(principal.Claims.Any(claim => claim.Type is AtprotoJwtOptions.CanonicalActorIdClaim or AtprotoJwtOptions.ExpectedCanonicalActorConcurrencyStampClaim)).IsFalse();
        await Assert.That(await service.ValidateSessionAsync(issued.Token, Guid.NewGuid(), CancellationToken.None)).IsNull();

        var oauthSignedSession = CreateSessionToken(keys.OAuthKey, userId, tenantId, did);
        await Assert.That(await service.ValidateSessionAsync(oauthSignedSession, tenantId, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task MultiAuthSelectorPeeksOnlyBoundedIssuerAndPreservesApiKeyAndKeycloakBranches()
    {
        using var keys = new TestKeyMaterial();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var atproto = CreateSessionToken(keys.SessionKey, userId, tenantId, "did:plc:selector");
        var atprotoContext = ContextWithBearer(atproto);
        await Assert.That(AuthenticationExtensions.SelectDefaultAuthenticationScheme(atprotoContext))
            .IsEqualTo(ApiAuthenticationSchemeNames.AtprotoSession);

        var local = CreateToken(
            keys.UnknownKey,
            LocalIdentityOptions.Issuer,
            LocalIdentityOptions.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D"))]);
        await Assert.That(AuthenticationExtensions.SelectDefaultAuthenticationScheme(ContextWithBearer(local)))
            .IsEqualTo(ApiAuthenticationSchemeNames.LocalIdentity);

        var keycloak = CreateToken(
            keys.UnknownKey,
            "https://keycloak.example/realms/event",
            "islamu-event-api",
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D"))]);
        await Assert.That(AuthenticationExtensions.SelectDefaultAuthenticationScheme(ContextWithBearer(keycloak)))
            .IsEqualTo(JwtBearerDefaults.AuthenticationScheme);

        var apiKeyContext = new DefaultHttpContext();
        apiKeyContext.Request.Headers["X-API-Key"] = "test-key";
        await Assert.That(AuthenticationExtensions.SelectDefaultAuthenticationScheme(apiKeyContext))
            .IsEqualTo(ApiAuthenticationSchemeNames.ApiKey);

        var oversized = ContextWithBearer(new string('a', AtprotoJwtOptions.MaximumSessionTokenBytes + 1));
        await Assert.That(AuthenticationExtensions.SelectDefaultAuthenticationScheme(oversized))
            .IsEqualTo(JwtBearerDefaults.AuthenticationScheme);
    }

    [Test]
    public async Task CurrentSessionBridgeRequiresExactAssertionAndRejectsReplay()
    {
        using var keys = new TestKeyMaterial();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string did = "did:plc:private-session";
        var bearer = CreateSessionToken(keys.SessionKey, userId, tenantId, did);
        var assertion = CreateSessionBridgeToken(keys.OAuthKey, tenantId, userId, did);

        var missingReplay = Substitute.For<IAtprotoBootstrapReplayRepository>();
        var missing = await AuthenticateCurrentSessionAsync(
            keys,
            tenantId,
            bearer,
            assertion: null,
            missingReplay);
        await Assert.That(missing.Succeeded).IsFalse();
        await missingReplay.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default, default, default);

        var routeVariant = await AuthenticateCurrentSessionAsync(
            keys,
            tenantId,
            bearer,
            assertion: null,
            missingReplay,
            requestPath: "/API/AUTH/ATPROTO/SESSION/CURRENT/");
        await Assert.That(routeVariant.Succeeded).IsFalse();

        var mismatchReplay = Substitute.For<IAtprotoBootstrapReplayRepository>();
        var mismatch = await AuthenticateCurrentSessionAsync(
            keys,
            tenantId,
            bearer,
            CreateSessionBridgeToken(keys.OAuthKey, tenantId, userId, "did:plc:other"),
            mismatchReplay);
        await Assert.That(mismatch.Succeeded).IsFalse();
        await mismatchReplay.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default, default, default);

        var replayStore = Substitute.For<IAtprotoBootstrapReplayRepository>();
        replayStore.TryConsumeAsync(
                Arg.Is<string>(value => value.StartsWith("session-bridge:", StringComparison.Ordinal)),
                tenantId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(true, false);
        var first = await AuthenticateCurrentSessionAsync(keys, tenantId, bearer, assertion, replayStore);
        var replayed = await AuthenticateCurrentSessionAsync(keys, tenantId, bearer, assertion, replayStore);

        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(replayed.Succeeded).IsFalse();
        await replayStore.Received(2).TryConsumeAsync(
            Arg.Any<string>(),
            tenantId,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SessionBridgeValidationRejectsPurposeAndBindingConfusion()
    {
        using var keys = new TestKeyMaterial();
        var service = CreateService(keys);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string did = "did:plc:bridge-binding";
        var valid = CreateSessionBridgeToken(keys.OAuthKey, tenantId, userId, did);

        await Assert.That(await service.ValidateSessionBridgeAsync(
            valid,
            tenantId,
            userId,
            did,
            HttpMethods.Get,
            AtprotoJwtOptions.CurrentSessionPath,
            CancellationToken.None)).IsNotNull();

        var attacks = new[]
        {
            string.Empty,
            CreateSessionBridgeToken(keys.OAuthKey, Guid.NewGuid(), userId, did),
            CreateSessionBridgeToken(keys.OAuthKey, tenantId, Guid.NewGuid(), did),
            CreateSessionBridgeToken(keys.OAuthKey, tenantId, userId, "did:plc:other"),
            CreateSessionBridgeToken(keys.OAuthKey, tenantId, userId, did, method: HttpMethods.Delete),
            CreateSessionBridgeToken(keys.OAuthKey, tenantId, userId, did, path: "/api/other"),
            CreateSessionBridgeToken(keys.OAuthKey, tenantId, userId, did, audience: AtprotoJwtOptions.BootstrapAudience),
            CreateBootstrapToken(keys.OAuthKey, tenantId),
            CreateSessionToken(keys.SessionKey, userId, tenantId, did),
            CreateSessionBridgeToken(keys.UnknownKey, tenantId, userId, did, keyId: "unknown"),
            CreateSessionBridgeToken(keys.OAuthKey, tenantId, userId, did, expires: DateTime.UtcNow.AddMinutes(5))
        };

        foreach (var attack in attacks)
        {
            await Assert.That(await service.ValidateSessionBridgeAsync(
                attack,
                tenantId,
                userId,
                did,
                HttpMethods.Get,
                AtprotoJwtOptions.CurrentSessionPath,
                CancellationToken.None)).IsNull();
        }
    }

    [Test]
    public async Task PrivateBridgeMetadataExcludesDiscoveryAndPinsSecurityControls()
    {
        var controllerType = typeof(AtprotoSessionController);
        var method = controllerType.GetMethod(nameof(AtprotoSessionController.BootstrapSession))
            ?? throw new InvalidOperationException("ATProto bridge action is missing.");

        await Assert.That(controllerType.GetCustomAttributes(typeof(ApiExplorerSettingsAttribute), true)
            .Cast<ApiExplorerSettingsAttribute>().Single().IgnoreApi).IsTrue();
        await Assert.That(controllerType.GetCustomAttributes(typeof(EndpointClassificationAttribute), true)).HasSingleItem();
        await Assert.That(controllerType.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>().Single().NoStore).IsTrue();
        await Assert.That(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().AuthenticationSchemes)
            .IsEqualTo(ApiAuthenticationSchemeNames.AtprotoBootstrap);
        await Assert.That(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>().Single().PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(method.GetCustomAttributes(typeof(RequestSizeLimitAttribute), true)).HasSingleItem();
        await Assert.That(method.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>().Single().Name).IsEqualTo("BootstrapAtprotoSession");

        var getCurrent = controllerType.GetMethod(nameof(AtprotoSessionController.GetCurrentSession))
            ?? throw new InvalidOperationException("Current ATProto session read action is missing.");
        var deleteCurrent = controllerType.GetMethod(nameof(AtprotoSessionController.DeleteCurrentSession))
            ?? throw new InvalidOperationException("Current ATProto session delete action is missing.");
        var refreshCurrent = controllerType.GetMethod("RefreshCurrentSession")
            ?? throw new InvalidOperationException("Current ATProto session refresh action is missing.");
        await Assert.That(getCurrent.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().AuthenticationSchemes)
            .IsEqualTo(ApiAuthenticationSchemeNames.AtprotoSession);
        await Assert.That(getCurrent.GetCustomAttributes(typeof(HttpGetAttribute), true)
            .Cast<HttpGetAttribute>().Single().Name).IsEqualTo("GetCurrentAtprotoSession");
        await Assert.That(deleteCurrent.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().AuthenticationSchemes)
            .IsEqualTo(ApiAuthenticationSchemeNames.AtprotoSession);
        await Assert.That(deleteCurrent.GetCustomAttributes(typeof(HttpDeleteAttribute), true)
            .Cast<HttpDeleteAttribute>().Single().Name).IsEqualTo("DeleteCurrentAtprotoSession");
        await Assert.That(refreshCurrent.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().AuthenticationSchemes)
            .IsEqualTo(ApiAuthenticationSchemeNames.AtprotoSession);
        await Assert.That(refreshCurrent.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>().Single().Name).IsEqualTo("RefreshCurrentAtprotoSession");
        await Assert.That(refreshCurrent.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>().Single().PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
    }

    private static AtprotoJwtService CreateService(TestKeyMaterial keys)
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var secretKey = call.ArgAt<string>(0);
                var value = secretKey == SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks
                    ? keys.OAuthRing
                    : keys.SessionRing;
                return SecretResolutionResult.Resolved(new ResolvedSecret(
                    secretKey,
                    value,
                    SecretSourceType.Infisical,
                    SecretScope.Instance,
                    null,
                    DateTimeOffset.UtcNow));
            });
        return new(resolver, Options.Create(new AtprotoJwtOptions()), TimeProvider.System);
    }

    private static string CreateBootstrapToken(
        ECDsa key,
        Guid tenantId,
        string audience = AtprotoJwtOptions.BootstrapAudience,
        string method = "POST",
        string path = AtprotoJwtOptions.BridgePath,
        DateTime? expires = null,
        DateTime? notBefore = null,
        string keyId = "oauth-active",
        DateTimeOffset? issuedAt = null,
        bool includeIssuedAt = true,
        string? classification = "person",
        Guid? canonicalActorId = null,
        Guid? expectedCanonicalActorConcurrencyStamp = null,
        IEnumerable<Claim>? extraClaims = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "event-blazor-bff"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(AtprotoJwtOptions.TenantClaim, tenantId.ToString("D")),
            new(AtprotoJwtOptions.DidClaim, "did:plc:bootstrap-user"),
            new(AtprotoJwtOptions.MethodClaim, method),
            new(AtprotoJwtOptions.PathClaim, path)
        };
        if (classification is not null)
        {
            claims.Add(new(AtprotoJwtOptions.ClassificationClaim, classification));
        }
        if (canonicalActorId is not null)
        {
            claims.Add(new(AtprotoJwtOptions.CanonicalActorIdClaim, canonicalActorId.Value.ToString("D")));
        }
        if (expectedCanonicalActorConcurrencyStamp is not null)
        {
            claims.Add(new(AtprotoJwtOptions.ExpectedCanonicalActorConcurrencyStampClaim, expectedCanonicalActorConcurrencyStamp.Value.ToString("D")));
        }
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        return CreateToken(
            key,
            AtprotoJwtOptions.BootstrapIssuer,
            audience,
            claims,
            expires,
            notBefore,
            keyId,
            issuedAt,
            includeIssuedAt);
    }

    private static string CreateSessionToken(ECDsa key, Guid userId, Guid tenantId, string did) => CreateToken(
        key,
        AtprotoJwtOptions.SessionIssuer,
        AtprotoJwtOptions.SessionAudience,
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
            new Claim(AtprotoJwtOptions.TenantClaim, tenantId.ToString("D")),
            new Claim(AtprotoJwtOptions.DidClaim, did),
            new Claim("auth_provider", "atproto")
        ],
        keyId: "session-active");

    private static string CreateSessionBridgeToken(
        ECDsa key,
        Guid tenantId,
        Guid userId,
        string did,
        string method = "GET",
        string path = AtprotoJwtOptions.CurrentSessionPath,
        string audience = AtprotoJwtOptions.SessionBridgeAudience,
        string keyId = "oauth-active",
        DateTime? expires = null) => CreateToken(
        key,
        AtprotoJwtOptions.SessionBridgeIssuer,
        audience,
        [
            new Claim(JwtRegisteredClaimNames.Sub, "event-blazor-bff"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new Claim(AtprotoJwtOptions.TenantClaim, tenantId.ToString("D")),
            new Claim(AtprotoJwtOptions.UserClaim, userId.ToString("D")),
            new Claim(AtprotoJwtOptions.DidClaim, did),
            new Claim(AtprotoJwtOptions.MethodClaim, method),
            new Claim(AtprotoJwtOptions.PathClaim, path)
        ],
        expires: expires,
        keyId: keyId);

    private static async Task<AuthenticateResult> AuthenticateCurrentSessionAsync(
        TestKeyMaterial keys,
        Guid tenantId,
        string bearer,
        string? assertion,
        IAtprotoBootstrapReplayRepository replayStore,
        string requestPath = AtprotoJwtOptions.CurrentSessionPath)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        var handler = new AtprotoSessionAuthenticationHandler(
            options,
            NullLoggerFactory.Instance,
            System.Text.Encodings.Web.UrlEncoder.Default,
            CreateService(keys),
            replayStore,
            tenantContext);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = requestPath;
        context.Request.Headers.Authorization = "Bearer " + bearer;
        if (assertion is not null)
        {
            context.Request.Headers[AtprotoJwtOptions.SessionBridgeHeaderName] = assertion;
        }

        await handler.InitializeAsync(
            new AuthenticationScheme(
                ApiAuthenticationSchemeNames.AtprotoSession,
                null,
                typeof(AtprotoSessionAuthenticationHandler)),
            context);
        return await handler.AuthenticateAsync();
    }

    private static string CreateToken(
        ECDsa key,
        string issuer,
        string audience,
        IEnumerable<Claim> claims,
        DateTime? expires = null,
        DateTime? notBefore = null,
        string keyId = "oauth-active",
        DateTimeOffset? issuedAt = null,
        bool includeIssuedAt = true)
    {
        var securityKey = new ECDsaSecurityKey(key) { KeyId = keyId };
        var tokenClaims = includeIssuedAt
            ? claims.Append(new Claim(
                JwtRegisteredClaimNames.Iat,
                (issuedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64))
            : claims;
        var token = new JwtSecurityToken(
            issuer,
            audience,
            tokenClaims,
            notBefore ?? DateTime.UtcNow.AddSeconds(-1),
            expires ?? DateTime.UtcNow.AddMinutes(1),
            new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256));
        token.Header[JwtHeaderParameterNames.Typ] = "JWT";
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateHs256Token(Guid tenantId)
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "oauth-active" };
        var token = new JwtSecurityToken(
            AtprotoJwtOptions.BootstrapIssuer,
            AtprotoJwtOptions.BootstrapAudience,
            [new Claim(AtprotoJwtOptions.TenantClaim, tenantId.ToString("D"))],
            DateTime.UtcNow.AddSeconds(-1),
            DateTime.UtcNow.AddMinutes(1),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateUnsignedToken(Guid tenantId)
    {
        static string Encode(string value) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(value));
        return $"{Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{Encode($"{{\"iss\":\"{AtprotoJwtOptions.BootstrapIssuer}\",\"aud\":\"{AtprotoJwtOptions.BootstrapAudience}\",\"tenant_id\":\"{tenantId:D}\"}}")}.";
    }

    private static DefaultHttpContext ContextWithBearer(string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer " + token;
        return context;
    }

    private sealed class TestKeyMaterial : IDisposable
    {
        public ECDsa OAuthKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public ECDsa SessionKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public ECDsa UnknownKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string OAuthRing => Ring(OAuthKey, "oauth-active");
        public string SessionRing => Ring(SessionKey, "session-active");

        public void Dispose()
        {
            OAuthKey.Dispose();
            SessionKey.Dispose();
            UnknownKey.Dispose();
        }

        private static string Ring(ECDsa key, string keyId)
        {
            var parameters = key.ExportParameters(true);
            return JsonSerializer.Serialize(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "EC", crv = "P-256", x = B64(parameters.Q.X!), y = B64(parameters.Q.Y!),
                        d = B64(parameters.D!), kid = keyId, use = "sig", alg = "ES256", status = "active"
                    }
                }
            });
        }

        private static string B64(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
