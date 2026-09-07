using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarpaNet.OAuth.Storage;
using Explore.Application.Contracts.Secrets;
using Explore.Atproto.Transport;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services.Federation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoOAuthClientFactoryTests
{
    private const string ClientId = "https://events.example.com/oauth/client-metadata.json";
    private const string CallbackUri = "https://events.example.com/signin-atproto";
    private const string Issuer = "https://issuer.example";

    [Test]
    public async Task FactoryResolvesInstanceSecretAndPreservesRetiredPinnedKey()
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks,
                null,
                Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks,
        CreatePrivateJwks(("new", "active"), ("old", "retired")),
        SecretSourceType.Infisical,
        SecretScope.Instance,
        null,
        DateTimeOffset.UtcNow)));
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var factory = new AtprotoOAuthClientFactory(
            resolver,
            Options.Create(new AtprotoInfrastructureOptions
            {
                PublicUrl = "https://events.example.com/",
                CallbackPath = "/signin-atproto"
            }),
            environment);

        await Assert.That((await factory.GetReadinessAsync(CancellationToken.None)).IsReady).IsTrue();
        using var lease = await factory.CreateAsync(
            "old",
            new MemoryOAuthStateStore(),
            new MemoryOAuthSessionStore(),
            CancellationToken.None);
        await Assert.That(lease.PinnedKeyId).IsEqualTo("old");
    }

    [Test]
    public async Task MissingOrMalformedKeyFailsReadinessWithoutSecretValueInReason()
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks,
        "private-canary",
        SecretSourceType.Infisical,
        SecretScope.Instance,
        null,
        DateTimeOffset.UtcNow)));
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var factory = new AtprotoOAuthClientFactory(
            resolver,
            Options.Create(new AtprotoInfrastructureOptions { PublicUrl = "https://events.example.com/" }),
            environment);

        var readiness = await factory.GetReadinessAsync(CancellationToken.None);
        await Assert.That(readiness.IsReady).IsFalse();
        await Assert.That(readiness.FailureCode).IsEqualTo("key_ring_unavailable");
        await Assert.That(readiness.FailureCode).DoesNotContain("private-canary");
    }

    [Test]
    public async Task OutboundPolicyRejectsPrivateMixedAnswersAndProductionLoopback()
    {
        var policy = new AtprotoOutboundPolicy(false);
        Action privateUri = () => policy.ValidateUri(new Uri("https://10.0.0.1/path"));
        Action mixed = () => policy.ValidateResolvedAddresses(
            "issuer.example",
            [System.Net.IPAddress.Parse("93.184.216.34"), System.Net.IPAddress.Loopback]);
        Action loopback = () => policy.ValidateUri(new Uri("http://127.0.0.1:8080/path"));

        await Assert.That(privateUri).Throws<HttpRequestException>();
        await Assert.That(mixed).Throws<HttpRequestException>();
        await Assert.That(loopback).Throws<HttpRequestException>();
    }

    [Test]
    public async Task ResolverFailureReturnsSafeReadinessAndCallerCancellationIsPreserved()
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SecretResolutionResult>(new HttpRequestException("private-canary")));
        var factory = CreateFactory(resolver);

        var readiness = await factory.GetReadinessAsync(CancellationToken.None);

        await Assert.That(readiness.IsReady).IsFalse();
        await Assert.That(readiness.FailureCode).IsEqualTo("secret_resolver_unavailable");
        await Assert.That(readiness.FailureCode).DoesNotContain("private-canary");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        resolver.ResolveAsync(Arg.Any<string>(), null, cancellation.Token)
            .Returns(Task.FromCanceled<SecretResolutionResult>(cancellation.Token));
        await Assert.That(async () => await factory.GetReadinessAsync(cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task InfrastructureAssertionAdapterRejectsExpiredMappingAndClientSubstitution()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var registry = new AtprotoAuthorizationServerRegistry(clock, TimeSpan.FromSeconds(1));
        registry.Register(new(
            Issuer,
            new Uri(Issuer + "/oauth/par"),
            new Uri(Issuer + "/oauth/token"),
            new Uri(Issuer + "/oauth/revoke")));
        var ring = InfrastructureAtprotoKeyRing.Parse(CreatePrivateJwks(("active", "active")));
        var recorder = new RecordingHandler();
        using var client = CreateAssertionClient(registry, ring, recorder);

        await Assert.That(async () => await SendRefreshAsync(
                client,
                "https://evil.example/oauth/client-metadata.json"))
            .Throws<AtprotoOAuthSecurityException>();
        clock.Advance(TimeSpan.FromSeconds(2));
        await Assert.That(async () => await SendRefreshAsync(client, ClientId))
            .Throws<AtprotoOAuthSecurityException>();
        await Assert.That(recorder.RequestCount).IsEqualTo(0);
    }

    [Test]
    public async Task InfrastructureAssertionAdapterPreservesContentHeadersAndRequiresBoundedNonce()
    {
        var registry = RegisteredRegistry();
        var ring = InfrastructureAtprotoKeyRing.Parse(CreatePrivateJwks(("active", "active")));
        var recorder = new RecordingHandler();
        using var client = CreateAssertionClient(registry, ring, recorder);
        using var request = RefreshRequest(ClientId);
        request.Content!.Headers.TryAddWithoutValidation("X-Content-Canary", "preserved");

        using var response = await client.SendAsync(request);

        await Assert.That(recorder.ContentCanary).IsEqualTo("preserved");
        await Assert.That(response.Headers.GetValues("DPoP-Nonce").Single()).IsEqualTo("nonce");

        recorder.ReturnInvalidNonce = true;
        await Assert.That(async () => await SendRefreshAsync(client, ClientId))
            .Throws<AtprotoOAuthSecurityException>();
    }

    [Test]
    public async Task InfrastructureKeyRingRejectsUnboundedNonCanonicalAndAmbiguousMaterial()
    {
        var valid = CreatePrivateJwks(("active", "active"));
        using var document = JsonDocument.Parse(valid);
        var key = document.RootElement.GetProperty("keys")[0];
        var x = key.GetProperty("x").GetString()!;
        var invalid = new[]
        {
            new string('x', 64 * 1024 + 1),
            valid.Replace(x, x[..^1] + (x[^1] == 'A' ? "B" : "A"), StringComparison.Ordinal),
            CreatePrivateJwks(("one", "active"), ("two", "active")),
            "{\"keys\":[{\"kty\":\"EC\",\"kty\":\"EC\"}]}",
            "{\"keys\":[]}",
            "{\"keys\":null}"
        };

        foreach (var serialized in invalid)
        {
            await Assert.That(InfrastructureAtprotoKeyRing.Parse(serialized).IsReady).IsFalse();
        }
    }

    [Test]
    public async Task SharedOutboundPolicyRejectsSpecialIpv4Ipv6AndCanonicalAuthorityMatrix()
    {
        var policy = new AtprotoOutboundPolicy(false);
        var unsafeUris = new[]
        {
            "https://0.0.0.0", "https://100.64.0.1", "https://169.254.1.1",
            "https://192.0.2.1", "https://198.18.0.1", "https://198.51.100.1",
            "https://203.0.113.1", "https://224.0.0.1", "https://[::]",
            "https://[::1]", "https://[2001:db8::1]", "https://[3fff::1]",
            "https://issuer.example./", "https://user@issuer.example", "http://issuer.example"
        };

        foreach (var value in unsafeUris)
        {
            await Assert.That(() => policy.ValidateUri(new Uri(value))).Throws<AtprotoOAuthSecurityException>();
        }

        await Assert.That(() => policy.ValidateResolvedAddresses(
                "issuer.example",
                [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.1")]))
            .Throws<AtprotoOAuthSecurityException>();
        var development = new AtprotoOutboundPolicy(true);
        development.ValidateUri(new Uri("http://localhost:8080"));
        await Assert.That(() => development.ValidateUri(new Uri("http://localhost.evil.example")))
            .Throws<AtprotoOAuthSecurityException>();
    }

    private static AtprotoOAuthClientFactory CreateFactory(ISecretResolver resolver)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        return new(
            resolver,
            Options.Create(new AtprotoInfrastructureOptions
            {
                PublicUrl = "https://events.example.com/",
                CallbackPath = "/signin-atproto"
            }),
            environment);
    }

    private static HttpClient CreateAssertionClient(
        AtprotoAuthorizationServerRegistry registry,
        InfrastructureAtprotoKeyRing ring,
        RecordingHandler recorder) =>
        new(new InfrastructureAtprotoPrivateKeyJwtHandler(
            registry,
            ring,
            ClientId,
            CallbackUri,
            InfrastructureAtprotoOAuthTransportFactory.RequiredScope,
            "active",
            recorder));

    private static AtprotoAuthorizationServerRegistry RegisteredRegistry()
    {
        var registry = new AtprotoAuthorizationServerRegistry();
        registry.Register(new(
            Issuer,
            new Uri(Issuer + "/oauth/par"),
            new Uri(Issuer + "/oauth/token"),
            new Uri(Issuer + "/oauth/revoke")));
        return registry;
    }

    private static async Task<HttpResponseMessage> SendRefreshAsync(HttpClient client, string clientId)
    {
        using var request = RefreshRequest(clientId);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage RefreshRequest(string clientId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Issuer + "/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = "refresh",
                ["client_id"] = clientId
            })
        };
        request.Headers.TryAddWithoutValidation("DPoP", "header.payload.signature");
        return request;
    }

    private static string CreatePrivateJwks(params (string Kid, string Status)[] definitions)
    {
        var keys = definitions.Select(definition =>
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = key.ExportParameters(true);
            return new
            {
                kty = "EC",
                crv = "P-256",
                x = Encode(parameters.Q.X!),
                y = Encode(parameters.Q.Y!),
                d = Encode(parameters.D!),
                kid = definition.Kid,
                use = "sig",
                alg = "ES256",
                status = definition.Status
            };
        });
        return JsonSerializer.Serialize(new { keys });
    }

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? ContentCanary { get; private set; }
        public bool ReturnInvalidNonce { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            ContentCanary = request.Content!.Headers.TryGetValues("X-Content-Canary", out var values)
                ? values.Single()
                : null;
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation(
                "DPoP-Nonce",
                ReturnInvalidNonce ? new string('n', 513) : "nonce");
            return Task.FromResult(response);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
