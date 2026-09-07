using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class AtprotoOAuthPublicationTests
{
    [Test]
    public async Task ClientMetadataPublishesExactConfidentialClientContract()
    {
        await using var factory = CreateFactory(CreatePrivateJwks(("oauth-client-2026-07", "active")));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://events.example.com")
        });

        using var response = await client.GetAsync("/oauth/client-metadata.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
        await Assert.That(response.Headers.Location).IsNull();
        await Assert.That(response.Headers.CacheControl?.Public).IsTrue();
        await Assert.That(response.Headers.CacheControl?.MaxAge).IsEqualTo(TimeSpan.FromMinutes(5));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        await Assert.That(root.GetProperty("client_id").GetString()).IsEqualTo("https://events.example.com/oauth/client-metadata.json");
        await Assert.That(root.GetProperty("redirect_uris")[0].GetString()).IsEqualTo("https://events.example.com/signin-atproto");
        await Assert.That(root.GetProperty("scope").GetString()).IsEqualTo("atproto transition:generic");
        await Assert.That(root.GetProperty("response_types").EnumerateArray().Select(value => value.GetString())
            .SequenceEqual(["code"])).IsTrue();
        await Assert.That(root.GetProperty("grant_types").EnumerateArray().Select(value => value.GetString())
            .SequenceEqual(["authorization_code", "refresh_token"])).IsTrue();
        await Assert.That(root.GetProperty("dpop_bound_access_tokens").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("token_endpoint_auth_method").GetString()).IsEqualTo("private_key_jwt");
        await Assert.That(root.GetProperty("token_endpoint_auth_signing_alg").GetString()).IsEqualTo("ES256");
        await Assert.That(root.GetProperty("jwks_uri").GetString()).IsEqualTo("https://events.example.com/oauth/jwks.json");
        await Assert.That(root.TryGetProperty("jwks", out _)).IsFalse();
    }

    [Test]
    public async Task JwksPublishesActiveAndRetiredPublicKeysInStableOrderWithoutPrivateMaterial()
    {
        var privateRing = CreatePrivateJwks(("z-active", "active"), ("a-retired", "retired"));
        await using var factory = CreateFactory(privateRing);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://events.example.com")
        });

        using var response = await client.GetAsync("/oauth/jwks.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var keys = document.RootElement.GetProperty("keys").EnumerateArray().ToArray();
        await Assert.That(keys.Select(key => key.GetProperty("kid").GetString())
            .SequenceEqual(["a-retired", "z-active"])).IsTrue();
        foreach (var key in keys)
        {
            await Assert.That(key.EnumerateObject().Select(property => property.Name))
                .IsEquivalentTo(["kty", "crv", "x", "y", "kid", "use", "alg"]);
            await Assert.That(key.GetProperty("kty").GetString()).IsEqualTo("EC");
            await Assert.That(key.GetProperty("crv").GetString()).IsEqualTo("P-256");
            await Assert.That(key.GetProperty("use").GetString()).IsEqualTo("sig");
            await Assert.That(key.GetProperty("alg").GetString()).IsEqualTo("ES256");
            await Assert.That(key.TryGetProperty("d", out _)).IsFalse();
            await Assert.That(key.TryGetProperty("status", out _)).IsFalse();
        }
    }

    [Test]
    public async Task PublicationOnNonCanonicalHostReturnsNotFoundWithoutRedirect()
    {
        await using var factory = CreateFactory(CreatePrivateJwks(("active", "active")));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://alternate.example.com")
        });

        using var response = await client.GetAsync("/oauth/client-metadata.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    [Arguments("/oauth/client-metadata.json")]
    [Arguments("/oauth/jwks.json")]
    public async Task OAuthPublicationOverHttpFailsClosedWithoutRedirect(string endpointPath)
    {
        await using var factory = CreateFactory(CreatePrivateJwks(("active", "active")));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://events.example.com")
        });

        using var response = await client.GetAsync(endpointPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    [Arguments("https://user:password@events.example.com", "https://events.example.com")]
    [Arguments("https://events.example.com.", "https://events.example.com.")]
    [Arguments("https://évents.example.com", "https://évents.example.com")]
    public async Task ClientMetadataRejectsNonCanonicalOrCredentialBearingPublicAuthority(
        string publicUrl,
        string requestBaseAddress)
    {
        await using var factory = CreateFactory(
            CreatePrivateJwks(("active", "active")),
            publicUrl: publicUrl);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(requestBaseAddress)
        });

        using var response = await client.GetAsync("/oauth/client-metadata.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    public async Task ClientMetadataNormalizesExplicitDefaultHttpsPort()
    {
        await using var factory = CreateFactory(
            CreatePrivateJwks(("active", "active")),
            publicUrl: "https://events.example.com:443");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://events.example.com")
        });

        using var response = await client.GetAsync("/oauth/client-metadata.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(document.RootElement.GetProperty("client_id").GetString()).IsEqualTo("https://events.example.com/oauth/client-metadata.json");
    }

    [Test]
    [Arguments("//evil.example/callback")]
    [Arguments("https://evil.example/callback")]
    [Arguments("signin-atproto")]
    [Arguments("/\\evil.example/callback")]
    [Arguments("\\\\evil.example\\callback")]
    [Arguments("/signin-atproto?next=https://evil.example")]
    [Arguments("/signin-atproto#fragment")]
    [Arguments("/oauth/../signin-atproto")]
    [Arguments("/oauth/%2e%2e/signin-atproto")]
    [Arguments("/signin%2fatproto")]
    public async Task ClientMetadataRejectsNonLocalOrAmbiguousCallbackPath(string callbackPath)
    {
        await using var factory = CreateFactory(
            CreatePrivateJwks(("active", "active")),
            callbackPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://events.example.com")
        });

        using var response = await client.GetAsync("/oauth/client-metadata.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    public async Task KeyProviderRejectsMissingMalformedUnknownDuplicateAndInvalidActiveKeyRings()
    {
        var missing = CreateProvider(null);
        var malformed = CreateProvider("{not-json");
        var unknownProperty = CreateProvider(CreatePrivateJwks(("active", "active")).Replace(
            "\"status\":\"active\"",
            "\"status\":\"active\",\"unexpected\":true",
            StringComparison.Ordinal));
        var duplicateKid = CreateProvider(CreatePrivateJwks(("duplicate", "active"), ("duplicate", "retired")));
        var noActive = CreateProvider(CreatePrivateJwks(("retired", "retired")));
        var multipleActive = CreateProvider(CreatePrivateJwks(("one", "active"), ("two", "active")));

        await Assert.That(missing.IsReady).IsFalse();
        await Assert.That(missing.FailureCode).IsEqualTo("missing_key_ring");
        await Assert.That(malformed.IsReady).IsFalse();
        await Assert.That(unknownProperty.IsReady).IsFalse();
        await Assert.That(duplicateKid.IsReady).IsFalse();
        await Assert.That(noActive.FailureCode).IsEqualTo("invalid_active_key_count");
        await Assert.That(multipleActive.FailureCode).IsEqualTo("invalid_active_key_count");
        await Assert.That(multipleActive.GetPublicKeys()).IsEmpty();
    }

    [Test]
    public async Task KeyProviderRejectsNonCanonicalBase64UrlCoordinatesAndPrivateScalar()
    {
        var canonicalRing = CreatePrivateJwks(("active", "active"));

        await Assert.That(CreateProvider(ReplaceWithNonCanonicalBase64Url(canonicalRing, "x")).IsReady).IsFalse();
        await Assert.That(CreateProvider(ReplaceWithNonCanonicalBase64Url(canonicalRing, "y")).IsReady).IsFalse();
        await Assert.That(CreateProvider(ReplaceWithNonCanonicalBase64Url(canonicalRing, "d")).IsReady).IsFalse();
    }

    [Test]
    public async Task KeyProviderSelectsActiveAndRetiredKeysByKidAndRejectsUnknownKid()
    {
        var provider = CreateProvider(CreatePrivateJwks(("current", "active"), ("previous", "retired")));

        await Assert.That(provider.IsReady).IsTrue();
        await Assert.That(provider.ActiveKeyId).IsEqualTo("current");
        using var current = provider.CreateActiveSigningKey();
        using var previous = provider.CreateSigningKey("previous");
        await Assert.That(current.KeySize).IsEqualTo(256);
        await Assert.That(previous.KeySize).IsEqualTo(256);
        var act = () => provider.CreateSigningKey("unknown");
        var exception = await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("ATProto OAuth client signing key is unavailable.");
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string privateRing,
        string? callbackPath = null,
        string publicUrl = "https://events.example.com")
    {
        return new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Atproto:PublicUrl", publicUrl);
            builder.UseSetting(AtprotoClientKeyProvider.ConfigurationKey, privateRing);
            if (callbackPath is not null)
            {
                builder.UseSetting("Atproto:CallbackPath", callbackPath);
            }
        });
    }

    private static AtprotoClientKeyProvider CreateProvider(string? privateRing)
    {
        return new AtprotoClientKeyProvider(Options.Create(new AtprotoClientKeyOptions
        {
            OAuthClientPrivateJwks = privateRing
        }));
    }

    private static string CreatePrivateJwks(params (string KeyId, string Status)[] specifications)
    {
        var keys = new List<object>();
        foreach (var specification in specifications)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = key.ExportParameters(true);
            keys.Add(new
            {
                kty = "EC",
                crv = "P-256",
                x = Base64Url(parameters.Q.X!),
                y = Base64Url(parameters.Q.Y!),
                d = Base64Url(parameters.D!),
                kid = specification.KeyId,
                use = "sig",
                alg = "ES256",
                status = specification.Status
            });
        }

        return JsonSerializer.Serialize(new
        {
            keys
        });
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ReplaceWithNonCanonicalBase64Url(string ring, string propertyName)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        using var document = JsonDocument.Parse(ring);
        var canonical = document.RootElement.GetProperty("keys")[0].GetProperty(propertyName).GetString()!;
        var lastCharacterIndex = alphabet.IndexOf(canonical[^1]);
        var nonCanonical = canonical[..^1] + alphabet[lastCharacterIndex + 1];
        return ring.Replace(
            $"\"{propertyName}\":\"{canonical}\"",
            $"\"{propertyName}\":\"{nonCanonical}\"",
            StringComparison.Ordinal);
    }
}
