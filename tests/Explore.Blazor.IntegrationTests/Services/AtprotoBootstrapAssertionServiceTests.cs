using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class AtprotoBootstrapAssertionServiceTests
{
    [Test]
    public async Task IssueBindsTenantDidClassificationAndExactRouteWithoutUserIdentity()
    {
        var tenantId = Guid.NewGuid();
        var service = CreateService();

        var token = service.Issue(
            tenantId,
            "did:plc:alice",
            "organization",
            HttpMethod.Post,
            AtprotoBootstrapAssertionService.BridgePath);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        await Assert.That(jwt.Issuer).IsEqualTo(AtprotoBootstrapAssertionService.Issuer);
        await Assert.That(jwt.Audiences).Contains(AtprotoBootstrapAssertionService.Audience);
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.TenantClaim).Value)
            .IsEqualTo(tenantId.ToString("D"));
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.MethodClaim).Value)
            .IsEqualTo(HttpMethods.Post);
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.PathClaim).Value)
            .IsEqualTo(AtprotoBootstrapAssertionService.BridgePath);
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.DidClaim).Value)
            .IsEqualTo("did:plc:alice");
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.ClassificationClaim).Value)
            .IsEqualTo("organization");
        await Assert.That(jwt.Claims.Any(claim => claim.Type is "user_id" or "email")).IsFalse();
    }

    [Test]
    public async Task IssueOmitsOrIncludesExactlyOneCompleteCanonicalActorTargetPair()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var canonicalActorId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();

        var omitted = new JwtSecurityTokenHandler().ReadJwtToken(service.Issue(
            tenantId,
            "did:plc:alice",
            "organization",
            HttpMethod.Post,
            AtprotoBootstrapAssertionService.BridgePath));
        var present = new JwtSecurityTokenHandler().ReadJwtToken(service.Issue(
            tenantId,
            "did:plc:alice",
            "organization",
            HttpMethod.Post,
            AtprotoBootstrapAssertionService.BridgePath,
            canonicalActorId,
            expectedConcurrencyStamp));

        await Assert.That(omitted.Claims.Any(claim => claim.Type is AtprotoBootstrapAssertionService.CanonicalActorIdClaim or AtprotoBootstrapAssertionService.ExpectedCanonicalActorConcurrencyStampClaim)).IsFalse();
        await Assert.That(present.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.CanonicalActorIdClaim).Value).IsEqualTo(canonicalActorId.ToString("D"));
        await Assert.That(present.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.ExpectedCanonicalActorConcurrencyStampClaim).Value).IsEqualTo(expectedConcurrencyStamp.ToString("D"));
    }

    [Test]
    public async Task HandlerAlwaysRemovesBrowserAssertionAndAddsServerAssertionOnlyForBoundBridgeCall()
    {
        var tenantId = Guid.NewGuid();
        var captured = new CapturingHandler();
        var handler = new BffCookieForwardingHandler(
            new BffAuthCookieStore(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<BffCookieForwardingHandler>.Instance,
            atprotoBootstrapAssertionService: CreateService())
        {
            InnerHandler = captured
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.test/api/auth/atproto/session");
        request.Headers.TryAddWithoutValidation(AtprotoBootstrapAssertionService.HeaderName, "browser-controlled");
        var canonicalActorId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        AtprotoBootstrapRequestOptions.Bind(request, tenantId, "did:plc:alice", "group", canonicalActorId, expectedConcurrencyStamp);

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(captured.Assertion).IsNotEqualTo("browser-controlled");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(captured.Assertion!);
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.TenantClaim).Value)
            .IsEqualTo(tenantId.ToString("D"));
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.DidClaim).Value)
            .IsEqualTo("did:plc:alice");
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.ClassificationClaim).Value)
            .IsEqualTo("group");
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.CanonicalActorIdClaim).Value)
            .IsEqualTo(canonicalActorId.ToString("D"));
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.ExpectedCanonicalActorConcurrencyStampClaim).Value)
            .IsEqualTo(expectedConcurrencyStamp.ToString("D"));

        using var unbound = new HttpRequestMessage(HttpMethod.Post, "https://api.example.test/api/auth/atproto/session");
        unbound.Headers.TryAddWithoutValidation(AtprotoBootstrapAssertionService.HeaderName, "browser-controlled");
        using var unboundResponse = await invoker.SendAsync(unbound, CancellationToken.None);
        await Assert.That(captured.Assertion).IsNull();
    }

    [Test]
    public async Task SessionBridgeAssertionUsesDistinctTrustDomainAndBindsAuthenticatedIdentity()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string did = "did:plc:alice";

        var token = CreateService().IssueSessionBridge(tenantId, userId, did, HttpMethod.Get);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        await Assert.That(jwt.Issuer).IsEqualTo(AtprotoBootstrapAssertionService.SessionBridgeIssuer);
        await Assert.That(jwt.Audiences).Contains(AtprotoBootstrapAssertionService.SessionBridgeAudience);
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.TenantClaim).Value)
            .IsEqualTo(tenantId.ToString("D"));
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.UserIdClaim).Value)
            .IsEqualTo(userId.ToString("D"));
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.DidClaim).Value)
            .IsEqualTo(did);
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.MethodClaim).Value)
            .IsEqualTo(HttpMethod.Get.Method);
        await Assert.That(jwt.Claims.Single(claim => claim.Type == AtprotoBootstrapAssertionService.PathClaim).Value)
            .IsEqualTo(AtprotoBootstrapAssertionService.SessionBridgePath);
    }

    private static AtprotoBootstrapAssertionService CreateService()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        var ring = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    x = Encode(parameters.Q.X!),
                    y = Encode(parameters.Q.Y!),
                    d = Encode(parameters.D!),
                    kid = "bootstrap-active",
                    use = "sig",
                    alg = "ES256",
                    status = "active"
                }
            }
        });
        var provider = new AtprotoClientKeyProvider(Options.Create(new AtprotoClientKeyOptions
        {
            OAuthClientPrivateJwks = ring
        }));
        return new(provider, TimeProvider.System);
    }

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Assertion { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assertion = request.Headers.TryGetValues(AtprotoBootstrapAssertionService.HeaderName, out var values)
                ? values.Single()
                : null;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
