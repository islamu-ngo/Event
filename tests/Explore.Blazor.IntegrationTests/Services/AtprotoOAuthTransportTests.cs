using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Explore.Atproto.Transport;
using Explore.Blazor.Authentication;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class AtprotoOAuthTransportTests
{
    private const int MaximumMetadataBytes = 64 * 1024;
    private const string Issuer = "https://issuer.example";
    private const string MetadataEndpoint = "https://issuer.example/.well-known/oauth-authorization-server";
    private const string ClientId = "https://events.example.com/oauth/client-metadata.json";
    private const string CallbackUri = "https://events.example.com/signin-atproto";
    private const string Scope = "atproto transition:generic";

    [Test]
    public async Task ValidMetadataRegistersCrossOriginEndpointsAndAssertionUsesIssuerAudience()
    {
        var registry = new AtprotoAuthorizationServerRegistry();
        var keyProvider = CreateKeyProvider();
        var recorder = new RecordingHandler((request, attempt) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return MetadataResponse(ValidMetadata(tokenEndpoint: "https://tokens.example/oauth/token"));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            response.Headers.TryAddWithoutValidation("DPoP-Nonce", $"nonce-{attempt}");
            return response;
        });
        using var client = CreateClient(registry, keyProvider, recorder, keyProvider.ActiveKeyId!);

        using var metadata = await client.GetAsync(MetadataEndpoint);
        await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://tokens.example/oauth/token")
        {
            Content = new StringContent(
                "grant_type=authorization_code&code=c&code_verifier=v&redirect_uri=https%3A%2F%2Fevents.example.com%2Fsignin-atproto&client_id="
                + Uri.EscapeDataString(ClientId),
                Encoding.UTF8,
                "application/x-www-form-urlencoded")
        };
        tokenRequest.Headers.TryAddWithoutValidation("DPoP", "header.payload.signature");
        using var tokenResponse = await client.SendAsync(tokenRequest);

        var form = ParseForm(recorder.RequestBodies.Last());
        var payload = DecodeJwtPart(form["client_assertion"], 1);
        var header = DecodeJwtPart(form["client_assertion"], 0);
        await Assert.That(payload.GetProperty("iss").GetString()).IsEqualTo(ClientId);
        await Assert.That(payload.GetProperty("sub").GetString()).IsEqualTo(ClientId);
        await Assert.That(payload.GetProperty("aud").GetString()).IsEqualTo(Issuer);
        await Assert.That((payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64())).IsLessThanOrEqualTo(60);
        await Assert.That(header.GetProperty("kid").GetString()).IsEqualTo(keyProvider.ActiveKeyId);
        await Assert.That(form["client_assertion_type"]).IsEqualTo("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
    }

    [Test]
    public async Task MetadataCapabilityMatrixFailsClosedBeforeRegistration()
    {
        var invalidDocuments = new[]
        {
            ValidMetadata().Replace("\"require_pushed_authorization_requests\":true", "\"require_pushed_authorization_requests\":false", StringComparison.Ordinal),
            ValidMetadata().Replace("\"private_key_jwt\"", "\"none\"", StringComparison.Ordinal),
            ValidMetadata().Replace("\"ES256\"", "\"RS256\"", StringComparison.Ordinal),
            ValidMetadata().Replace("\"S256\"", "\"plain\"", StringComparison.Ordinal),
            ValidMetadata().Replace("\"authorization_code\",\"refresh_token\"", "\"authorization_code\"", StringComparison.Ordinal),
            ValidMetadata().Replace("\"response_types_supported\":[\"code\"]", "\"response_types_supported\":[]", StringComparison.Ordinal),
            ValidMetadata().Replace("\"authorization_response_iss_parameter_supported\":true", "\"authorization_response_iss_parameter_supported\":false", StringComparison.Ordinal),
            ValidMetadata().Replace("\"client_id_metadata_document_supported\":true", "\"client_id_metadata_document_supported\":false", StringComparison.Ordinal),
            ValidMetadata().Replace("\"scopes_supported\":[\"atproto\"]", "\"scopes_supported\":[\"openid\"]", StringComparison.Ordinal),
            ValidMetadata().Replace("\"require_request_uri_registration\":true", "\"require_request_uri_registration\":false", StringComparison.Ordinal),
            ValidMetadata().Replace("\"issuer\":\"https://issuer.example\"", "\"issuer\":\"https://other.example\"", StringComparison.Ordinal),
            ValidMetadata().Replace("\"token_endpoint\":\"https://issuer.example/oauth/token\"", "\"token_endpoint\":\"http://issuer.example/oauth/token\"", StringComparison.Ordinal),
            ValidMetadata().Replace("\"issuer\":", "\"issuer\":\"https://duplicate.example\",\"issuer\":", StringComparison.Ordinal)
        };

        foreach (var document in invalidDocuments)
        {
            var registry = new AtprotoAuthorizationServerRegistry();
            var recorder = new RecordingHandler((_, _) => MetadataResponse(document));
            using var client = new HttpClient(new AtprotoAuthorizationServerMetadataHandler(
                registry,
                new AtprotoOutboundPolicy(false),
                recorder));

            var act = () => client.GetAsync(MetadataEndpoint);
            await Assert.That(act).Throws<AtprotoOAuthSecurityException>();
            await Assert.That(registry.TryResolve(new Uri("https://issuer.example/oauth/token"), out _)).IsFalse();
        }
    }

    [Test]
    public async Task MetadataHttpEnvelopeRejectsNonOkWrongContentTypeAndOversize()
    {
        var responses = new Func<HttpResponseMessage>[]
        {
            () => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(ValidMetadata(), Encoding.UTF8, "application/json") },
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ValidMetadata(), Encoding.UTF8, "text/plain") },
            () => CreateOversizedMetadataResponse()
        };

        foreach (var responseFactory in responses)
        {
            var registry = new AtprotoAuthorizationServerRegistry();
            using var client = new HttpClient(new AtprotoAuthorizationServerMetadataHandler(
                registry,
                new AtprotoOutboundPolicy(false),
                new RecordingHandler((_, _) => responseFactory())));
            Func<Task> act = () => client.GetAsync(MetadataEndpoint);
            await Assert.That(act).Throws<AtprotoOAuthSecurityException>();
        }
    }

    [Test]
    public async Task MetadataIndependentCapabilityAndCanonicalEndpointMatrixFailsClosed()
    {
        var invalidDocuments = new List<string>();
        foreach (var property in new[]
                 {
                     "require_pushed_authorization_requests",
                     "token_endpoint_auth_methods_supported",
                     "token_endpoint_auth_signing_alg_values_supported",
                     "dpop_signing_alg_values_supported",
                     "grant_types_supported",
                     "response_types_supported",
                     "code_challenge_methods_supported",
                     "authorization_response_iss_parameter_supported",
                     "client_id_metadata_document_supported",
                     "scopes_supported"
                 })
        {
            invalidDocuments.Add(MutateMetadata(root => root.Remove(property)));
        }

        invalidDocuments.Add(MutateMetadata(root => root["require_pushed_authorization_requests"] = false));
        invalidDocuments.Add(MutateMetadata(root => root["token_endpoint_auth_methods_supported"] = new JsonArray("none")));
        invalidDocuments.Add(MutateMetadata(root => root["token_endpoint_auth_signing_alg_values_supported"] = new JsonArray("RS256")));
        invalidDocuments.Add(MutateMetadata(root => root["dpop_signing_alg_values_supported"] = new JsonArray("RS256")));
        invalidDocuments.Add(MutateMetadata(root => root["grant_types_supported"] = new JsonArray("refresh_token")));
        invalidDocuments.Add(MutateMetadata(root => root["grant_types_supported"] = new JsonArray("authorization_code")));
        invalidDocuments.Add(MutateMetadata(root => root["response_types_supported"] = new JsonArray("token")));
        invalidDocuments.Add(MutateMetadata(root => root["code_challenge_methods_supported"] = new JsonArray("plain")));
        invalidDocuments.Add(MutateMetadata(root => root["authorization_response_iss_parameter_supported"] = false));
        invalidDocuments.Add(MutateMetadata(root => root["client_id_metadata_document_supported"] = false));
        invalidDocuments.Add(MutateMetadata(root => root["scopes_supported"] = new JsonArray("openid")));
        invalidDocuments.Add(MutateMetadata(root => root["require_request_uri_registration"] = false));
        invalidDocuments.Add(MutateMetadata(root => root["scopes_supported"] = new JsonArray("atproto", 42)));
        foreach (var endpoint in new[]
                 {
                     "authorization_endpoint",
                     "pushed_authorization_request_endpoint",
                     "token_endpoint",
                     "revocation_endpoint"
                 })
        {
            invalidDocuments.Add(MutateMetadata(root => root[endpoint] = "http://issuer.example/oauth/unsafe"));
            invalidDocuments.Add(MutateMetadata(root => root[endpoint] = "https://127.0.0.1/oauth/unsafe"));
        }

        foreach (var issuer in new[]
                 {
                     " https://issuer.example",
                     "https://issuer.example:443",
                     "https://user@issuer.example",
                     "https://issuer.example./",
                     "https://issuer.example?tenant=1",
                     "https://éxample.com"
                 })
        {
            invalidDocuments.Add(MutateMetadata(root => root["issuer"] = issuer));
        }

        invalidDocuments.Add(MutateMetadata(root =>
            root["token_endpoint"] = root["pushed_authorization_request_endpoint"]!.GetValue<string>()));
        invalidDocuments.Add("{");

        foreach (var document in invalidDocuments)
        {
            var registry = new AtprotoAuthorizationServerRegistry();
            using var client = new HttpClient(new AtprotoAuthorizationServerMetadataHandler(
                registry,
                new AtprotoOutboundPolicy(false),
                new RecordingHandler((_, _) => MetadataResponse(document))));
            Func<Task> act = () => client.GetAsync(MetadataEndpoint);
            await Assert.That(act).Throws<AtprotoOAuthSecurityException>();
            await Assert.That(registry.TryResolve(new Uri("https://issuer.example/oauth/token"), out _)).IsFalse();
        }
    }

    [Test]
    public async Task MetadataAllowsAbsentRequestUriFlagAndPreservesEndpointQueryExactly()
    {
        var registry = new AtprotoAuthorizationServerRegistry();
        var document = MutateMetadata(root =>
        {
            root.Remove("require_request_uri_registration");
            root["token_endpoint"] = "https://tokens.example/oauth/token?tenant=one";
        });
        var recorder = new RecordingHandler((request, _) => request.Method == HttpMethod.Get
            ? MetadataResponse(document)
            : NonceResponse());
        using var client = CreateClient(registry, CreateKeyProvider(), recorder, "active");

        using var metadata = await client.GetAsync(MetadataEndpoint);
        using var token = await SendFormAsync(
            client,
            "https://tokens.example/oauth/token?tenant=one",
            RefreshForm());

        await Assert.That(registry.TryResolve(new Uri("https://tokens.example/oauth/token?tenant=one"), out _)).IsTrue();
        await Assert.That(registry.TryResolve(new Uri("https://tokens.example/oauth/token?tenant=two"), out _)).IsFalse();
        await Assert.That(ParseForm(recorder.RequestBodies.Last())).ContainsKey("client_assertion");
    }

    private static HttpResponseMessage CreateOversizedMetadataResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[MaximumMetadataBytes + 1])
        };
        response.Content.Headers.ContentType = new("application/json");
        return response;
    }

    [Test]
    public async Task ParCodeRefreshAndRevokeEachReceiveFreshIssuerBoundAssertion()
    {
        var registry = RegisteredRegistry();
        var keyProvider = CreateKeyProvider();
        var recorder = new RecordingHandler((_, _) => NonceResponse());
        using var client = CreateClient(registry, keyProvider, recorder, keyProvider.ActiveKeyId!);

        using var par = await SendFormAsync(
            client,
            "https://issuer.example/oauth/par",
            ParForm());
        using var code = await SendFormAsync(
            client,
            "https://issuer.example/oauth/token",
            "grant_type=authorization_code&code=c&code_verifier=v&redirect_uri=https%3A%2F%2Fevents.example.com%2Fsignin-atproto&client_id=" + Uri.EscapeDataString(ClientId));
        using var refresh = await SendFormAsync(
            client,
            "https://issuer.example/oauth/token",
            "grant_type=refresh_token&refresh_token=r&client_id=" + Uri.EscapeDataString(ClientId));
        using var revoke = await SendFormAsync(
            client,
            "https://issuer.example/oauth/revoke",
            "token=r&token_type_hint=refresh_token");

        var assertions = recorder.RequestBodies.Select(body => ParseForm(body)["client_assertion"]).ToArray();
        await Assert.That(assertions).Count().IsEqualTo(4);
        await Assert.That(assertions.Select(assertion => DecodeJwtPart(assertion, 1).GetProperty("aud").GetString())
            .All(audience => audience == Issuer)).IsTrue();
        var jwtIds = assertions.Select(assertion => DecodeJwtPart(assertion, 1).GetProperty("jti").GetString()).ToArray();
        await Assert.That(jwtIds.Distinct().Count()).IsEqualTo(jwtIds.Length);
    }

    [Test]
    public async Task ClassifiedFormsRejectMissingNonceDuplicatesUnknownEndpointAndNonForm()
    {
        var registry = RegisteredRegistry();
        var keyProvider = CreateKeyProvider();
        var noNonce = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(registry, keyProvider, noNonce, keyProvider.ActiveKeyId!);

        Func<Task> missingNonce = () => SendFormAsync(
            client,
            "https://issuer.example/oauth/par",
            ParForm());
        await Assert.That(missingNonce).Throws<AtprotoOAuthSecurityException>();

        Func<Task> duplicate = () => SendFormAsync(client, "https://issuer.example/oauth/token", "grant_type=refresh_token&refresh_token=r&client_assertion=x");
        await Assert.That(duplicate).Throws<AtprotoOAuthSecurityException>();

        Func<Task> unmapped = () => client.PostAsync("https://unknown.example/oauth/token", Form("grant_type=refresh_token&refresh_token=r"));
        await Assert.That(unmapped).Throws<AtprotoOAuthSecurityException>();

        Func<Task> nonForm = () => client.PostAsync("https://issuer.example/oauth/token", new StringContent("{}", Encoding.UTF8, "application/json"));
        await Assert.That(nonForm).Throws<AtprotoOAuthSecurityException>();
    }

    [Test]
    public async Task EverySuccessfulOAuthOperationRejectsMissingAndInvalidDpopNonceIndependently()
    {
        var operations = new[]
        {
            (Endpoint: "https://issuer.example/oauth/par", Body: ParForm()),
            (Endpoint: "https://issuer.example/oauth/token", Body: CodeForm()),
            (Endpoint: "https://issuer.example/oauth/token", Body: RefreshForm()),
            (Endpoint: "https://issuer.example/oauth/revoke", Body: "token=refresh&token_type_hint=refresh_token")
        };

        foreach (var operation in operations)
        {
            foreach (var nonce in new string?[] { null, new('x', 513) })
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                if (nonce is not null)
                {
                    response.Headers.TryAddWithoutValidation("DPoP-Nonce", nonce);
                }

                using var client = CreateClient(
                    RegisteredRegistry(),
                    CreateKeyProvider(),
                    new FixedResponseHandler(() => response),
                    "active");

                Func<Task> act = () => SendFormAsync(client, operation.Endpoint, operation.Body);
                await Assert.That(act).Throws<AtprotoOAuthSecurityException>();
            }
        }
    }

    [Test]
    public async Task ExactFormPolicyRejectsClientCallbackMissingEmptyAndExtraFieldSubstitution()
    {
        var registry = RegisteredRegistry();
        var keyProvider = CreateKeyProvider();
        using var client = CreateClient(
            registry,
            keyProvider,
            new RecordingHandler((_, _) => NonceResponse()),
            keyProvider.ActiveKeyId!);
        var cases = new[]
        {
            ("https://issuer.example/oauth/par", ParForm().Replace(Uri.EscapeDataString(ClientId), Uri.EscapeDataString("https://evil.example/client"), StringComparison.Ordinal)),
            ("https://issuer.example/oauth/par", ParForm().Replace(Uri.EscapeDataString(CallbackUri), Uri.EscapeDataString("https://evil.example/callback"), StringComparison.Ordinal)),
            ("https://issuer.example/oauth/par", ParForm().Replace("&state=state", string.Empty, StringComparison.Ordinal)),
            ("https://issuer.example/oauth/par", ParForm().Replace("state=state", "state=", StringComparison.Ordinal)),
            ("https://issuer.example/oauth/par", ParForm() + "&foo=bar"),
            ("https://issuer.example/oauth/token", CodeForm().Replace("code_verifier=verifier&", string.Empty, StringComparison.Ordinal)),
            ("https://issuer.example/oauth/token", CodeForm().Replace("code=code", "code=", StringComparison.Ordinal)),
            ("https://issuer.example/oauth/token", CodeForm().Replace(Uri.EscapeDataString(CallbackUri), Uri.EscapeDataString("https://evil.example/callback"), StringComparison.Ordinal)),
            ("https://issuer.example/oauth/token", CodeForm() + "&refresh_token=refresh"),
            ("https://issuer.example/oauth/token", RefreshForm().Replace("refresh_token=refresh", "refresh_token=", StringComparison.Ordinal)),
            ("https://issuer.example/oauth/token", RefreshForm() + "&code=code"),
            ("https://issuer.example/oauth/token", RefreshForm().Replace(Uri.EscapeDataString(ClientId), Uri.EscapeDataString("https://evil.example/client"), StringComparison.Ordinal)),
            ("https://issuer.example/oauth/revoke", "token=&token_type_hint=refresh_token"),
            ("https://issuer.example/oauth/revoke", "token=refresh&token_type_hint=refresh_token&client_id=" + Uri.EscapeDataString(ClientId))
        };

        foreach (var (endpoint, form) in cases)
        {
            Func<Task> act = () => SendFormAsync(client, endpoint, form);
            await Assert.That(act).Throws<AtprotoOAuthSecurityException>();
        }
    }

    [Test]
    public async Task ExpiredEndpointTrustRejectsOAuthFormInsteadOfFallingBackUnauthenticated()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var registry = new AtprotoAuthorizationServerRegistry(clock, TimeSpan.FromSeconds(1));
        registry.Register(new(
            Issuer,
            new Uri("https://issuer.example/oauth/par"),
            new Uri("https://issuer.example/oauth/token"),
            new Uri("https://issuer.example/oauth/revoke")));
        var recorder = new RecordingHandler((_, _) => NonceResponse());
        using var client = CreateClient(registry, CreateKeyProvider(), recorder, "active");
        clock.Advance(TimeSpan.FromSeconds(2));

        Func<Task> act = () => SendFormAsync(
            client,
            "https://issuer.example/oauth/token",
            RefreshForm());

        await Assert.That(act).Throws<AtprotoOAuthSecurityException>();
        await Assert.That(recorder.RequestBodies).IsEmpty();
    }

    [Test]
    public async Task RetryCreatesFreshAssertionAndPinnedRetiredKeySurvivesRotation()
    {
        var registry = RegisteredRegistry();
        var ring = CreatePrivateJwks(("new", "active"), ("old", "retired"));
        var keyProvider = CreateKeyProvider(ring);
        var recorder = new RecordingHandler((_, attempt) =>
        {
            var response = new HttpResponseMessage(attempt == 1 ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(attempt == 1 ? "{\"error\":\"use_dpop_nonce\"}" : "{}")
            };
            response.Headers.TryAddWithoutValidation("DPoP-Nonce", $"nonce-{attempt}");
            return response;
        });
        using var client = CreateClient(registry, keyProvider, recorder, "old");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await SendFormAsync(
                client,
                "https://issuer.example/oauth/token",
                "grant_type=refresh_token&refresh_token=r&client_id=" + Uri.EscapeDataString(ClientId));
        }

        var assertions = recorder.RequestBodies.Select(body => ParseForm(body)["client_assertion"]).ToArray();
        await Assert.That(assertions).Count().IsEqualTo(2);
        await Assert.That(DecodeJwtPart(assertions[0], 0).GetProperty("kid").GetString()).IsEqualTo("old");
        await Assert.That(DecodeJwtPart(assertions[1], 0).GetProperty("kid").GetString()).IsEqualTo("old");
        await Assert.That(DecodeJwtPart(assertions[0], 1).GetProperty("jti").GetString()).IsNotEqualTo(DecodeJwtPart(assertions[1], 1).GetProperty("jti").GetString());
    }

    [Test]
    public async Task RealCarpaParAndCodeNonceRetriesCreateFreshPinnedIssuerAssertions()
    {
        var registry = new AtprotoAuthorizationServerRegistry();
        var keyProvider = CreateKeyProvider(CreatePrivateJwks(("old", "retired"), ("new", "active")));
        var recorder = new CarpaRetryHandler();
        using var httpClient = CreateClient(registry, keyProvider, recorder, "old");
        var stateStore = new MemoryOAuthStateStore();
        var sessionStore = new MemoryOAuthSessionStore();
        using var session = new OAuthSession(new OAuthClientConfig
        {
            ClientId = ClientId,
            RedirectUri = CallbackUri,
            Scope = Scope,
            HttpClient = httpClient,
            StateStore = stateStore,
            SessionStore = sessionStore,
            JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        });

        _ = await session.AuthorizeAsync("https://pds.example", cancellationToken: CancellationToken.None);
        var state = ParseForm(recorder.ParBodies[0])["state"];
        using var restoredClient = await session.CallbackAsync(
            CallbackUri + "?code=authorization-code&state=" + Uri.EscapeDataString(state),
            CancellationToken.None);

        await Assert.That(recorder.ParBodies).Count().IsEqualTo(2);
        await Assert.That(recorder.TokenBodies).Count().IsEqualTo(2);
        var assertions = recorder.ParBodies.Concat(recorder.TokenBodies)
            .Select(body => ParseForm(body)["client_assertion"])
            .ToArray();
        await Assert.That(assertions).Count().IsEqualTo(4);
        await Assert.That(assertions.Select(assertion => DecodeJwtPart(assertion, 0).GetProperty("kid").GetString())
            .All(kid => kid == "old")).IsTrue();
        await Assert.That(assertions.Select(assertion => DecodeJwtPart(assertion, 1).GetProperty("aud").GetString())
            .All(audience => audience == Issuer)).IsTrue();
        var jwtIds = assertions.Select(assertion => DecodeJwtPart(assertion, 1).GetProperty("jti").GetString()).ToArray();
        await Assert.That(jwtIds.Distinct().Count()).IsEqualTo(jwtIds.Length);
    }

    [Test]
    public async Task RestoredExpiredCarpaSessionRefreshesAndRevokesWithPinnedKeyThenAlwaysDeletesLocally()
    {
        const string did = "did:plc:restored-user";
        var registry = new AtprotoAuthorizationServerRegistry();
        var keyProvider = CreateKeyProvider(CreatePrivateJwks(("old", "retired"), ("new", "active")));
        var recorder = new RestoredSessionHandler();
        using var httpClient = CreateClient(registry, keyProvider, recorder, "old");
        var sessionStore = new MemoryOAuthSessionStore();
        using var dpopKey = await DPoPKeyPair.GenerateAsync();
        await sessionStore.StoreAsync(did, new OAuthSessionData
        {
            DPoPKey = dpopKey.ExportKeyPair(),
            TokenSet = new TokenSet
            {
                Issuer = Issuer,
                Sub = did,
                Audience = "https://pds.example",
                Scope = Scope,
                AccessToken = "expired-access",
                RefreshToken = "refresh-before-rotation",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            ClientId = ClientId,
            RedirectUri = CallbackUri,
            Scope = Scope
        });

        using (var discovery = new AuthorizationServerDiscovery(httpClient))
        using (var tokenProvider = new DPoPTokenProvider(
                   httpClient,
                   sessionStore,
                   discovery,
                   clientId: ClientId,
                   redirectUri: CallbackUri,
                   scope: Scope))
        {
            await Assert.That((await tokenProvider.RestoreSessionAsync(did))).IsTrue();
            await Assert.That((await tokenProvider.GetAccessTokenAsync())).IsEqualTo("refreshed-access");
        }

        using (var session = new OAuthSession(new OAuthClientConfig
        {
            ClientId = ClientId,
            RedirectUri = CallbackUri,
            Scope = Scope,
            HttpClient = httpClient,
            StateStore = new MemoryOAuthStateStore(),
            SessionStore = sessionStore,
            JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        }))
        {
            await session.RevokeAsync(did);
        }

        await Assert.That(recorder.TokenBodies).HasSingleItem();
        await Assert.That(recorder.RevocationBodies).HasSingleItem();
        var refreshAssertion = ParseForm(recorder.TokenBodies.Single())["client_assertion"];
        var revokeAssertion = ParseForm(recorder.RevocationBodies.Single())["client_assertion"];
        foreach (var assertion in new[] { refreshAssertion, revokeAssertion })
        {
            await Assert.That(DecodeJwtPart(assertion, 0).GetProperty("kid").GetString()).IsEqualTo("old");
            await Assert.That(DecodeJwtPart(assertion, 1).GetProperty("aud").GetString()).IsEqualTo(Issuer);
        }

        await Assert.That(DecodeJwtPart(refreshAssertion, 1).GetProperty("jti").GetString()).IsNotEqualTo(DecodeJwtPart(revokeAssertion, 1).GetProperty("jti").GetString());
        await Assert.That((await sessionStore.GetAsync(did))).IsNull().Because("local cleanup must complete even when a successful remote revocation omits its mandatory nonce");
    }

    [Test]
    public async Task ConcurrentIssuersAndPinnedSessionsNeverCrossAudienceOrKey()
    {
        var issuerA = "https://issuer-a.example";
        var issuerB = "https://issuer-b.example";
        var recorderA = new RecordingHandler((_, _) => NonceResponse());
        var recorderB = new RecordingHandler((_, _) => NonceResponse());
        var keysA = CreateKeyProvider(CreatePrivateJwks(("key-a", "active")));
        var keysB = CreateKeyProvider(CreatePrivateJwks(("key-b", "active")));
        using var clientA = CreateClient(RegisteredRegistry(issuerA), keysA, recorderA, "key-a");
        using var clientB = CreateClient(RegisteredRegistry(issuerB), keysB, recorderB, "key-b");

        var responses = await Task.WhenAll(
            SendFormAsync(clientA, issuerA + "/oauth/token", RefreshForm()),
            SendFormAsync(clientB, issuerB + "/oauth/token", RefreshForm()));
        foreach (var response in responses)
        {
            response.Dispose();
        }

        await AssertAssertionBinding(recorderA.RequestBodies.Single(), issuerA, "key-a");
        await AssertAssertionBinding(recorderB.RequestBodies.Single(), issuerB, "key-b");
    }

    [Test]
    public async Task OutboundPolicyRejectsPrivateAndAllowsOnlyExplicitDevelopmentLoopback()
    {
        var production = new AtprotoOutboundPolicy(false);
        production.ValidateUri(new Uri("https://example.com/path"));
        foreach (var value in new[] { "http://127.0.0.1", "https://127.0.0.1", "https://10.0.0.1", "https://169.254.169.254", "https://[::1]", "https://[fd00::1]" })
        {
            var act = () => production.ValidateUri(new Uri(value));
            await Assert.That(act).Throws<AtprotoOAuthSecurityException>();
        }
        Action mixedDns = () => production.ValidateResolvedAddresses(
            "issuer.example",
            [IPAddress.Parse("93.184.216.34"), IPAddress.Loopback]);
        await Assert.That(mixedDns).Throws<AtprotoOAuthSecurityException>();

        var development = new AtprotoOutboundPolicy(true);
        development.ValidateUri(new Uri("http://127.0.0.1:8080/path"));
        Action deceptiveLoopback = () => development.ValidateUri(new Uri("http://localhost.evil.example/path"));
        await Assert.That(deceptiveLoopback).Throws<AtprotoOAuthSecurityException>();
    }

    [Test]
    public async Task RegistryRegistrationIsAtomicAndExpires()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var registry = new AtprotoAuthorizationServerRegistry(clock, TimeSpan.FromMinutes(1));
        registry.Register(new(
            Issuer,
            new Uri("https://issuer.example/oauth/par"),
            new Uri("https://issuer.example/oauth/token"),
            null));

        Action collision = () => registry.Register(new(
            "https://other.example",
            new Uri("https://other.example/oauth/par"),
            new Uri("https://issuer.example/oauth/token"),
            null));
        await Assert.That(collision).Throws<AtprotoOAuthSecurityException>();
        await Assert.That(registry.TryResolve(new Uri("https://other.example/oauth/par"), out _)).IsFalse();

        clock.Advance(TimeSpan.FromMinutes(2));
        await Assert.That(registry.TryResolve(new Uri("https://issuer.example/oauth/token"), out _)).IsFalse();
        Action sameProfileEndpoint = () => registry.Register(new(
            Issuer,
            new Uri("https://issuer.example/oauth/shared"),
            new Uri("https://issuer.example/oauth/shared"),
            null));
        await Assert.That(sameProfileEndpoint).Throws<AtprotoOAuthSecurityException>();
        await Task.CompletedTask;
    }

    [Test]
    public async Task ClassifiedRequestRejectsMissingOrDuplicateDpopAndMalformedFormEncoding()
    {
        var registry = RegisteredRegistry();
        var keyProvider = CreateKeyProvider();
        using var client = CreateClient(registry, keyProvider, new RecordingHandler((_, _) => NonceResponse()), keyProvider.ActiveKeyId!);

        using var missing = new HttpRequestMessage(HttpMethod.Post, "https://issuer.example/oauth/token")
        {
            Content = new StringContent("grant_type=refresh_token&refresh_token=r", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        Func<Task> missingProof = () => client.SendAsync(missing);
        await missingProof.ShouldThrowAsync<AtprotoOAuthSecurityException>();

        using var duplicate = new HttpRequestMessage(HttpMethod.Post, "https://issuer.example/oauth/token")
        {
            Content = new StringContent("grant_type=refresh_token&refresh_token=r", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        duplicate.Headers.TryAddWithoutValidation("DPoP", new[] { "a.b.c", "d.e.f" });
        Func<Task> duplicateProof = () => client.SendAsync(duplicate);
        await duplicateProof.ShouldThrowAsync<AtprotoOAuthSecurityException>();

        using var malformed = new HttpRequestMessage(HttpMethod.Post, "https://issuer.example/oauth/token")
        {
            Content = new StringContent("grant_type=refresh_token&refresh_token=%zz", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        malformed.Headers.TryAddWithoutValidation("DPoP", "a.b.c");
        Func<Task> malformedEncoding = () => client.SendAsync(malformed);
        await malformedEncoding.ShouldThrowAsync<AtprotoOAuthSecurityException>();
    }

    [Test]
    public async Task BoundedHandlerPreservesCallerCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var client = new HttpClient(new AtprotoBoundedResponseHandler(100, new CancellationHandler()));

        Func<Task> act = () => client.GetAsync("https://example.com", source.Token);
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task PinnedConnectorPreservesCallerCancellationInsteadOfRelabelingIt()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = async () => await AtprotoHardenedHttpClient.ConnectPinnedAsync(
            "issuer.example",
            443,
            new AtprotoOutboundPolicy(false),
            static (_, _) => new([IPAddress.Parse("93.184.216.34")]),
            static (_, _, token) => ValueTask.FromException<Stream>(new OperationCanceledException(token)),
            cancellation.Token);

        await Assert.That(act).ThrowsExactly<OperationCanceledException>();
    }

    [Test]
    public async Task HardenedPrimaryDisablesRedirectStateAndConnectsOnlyResolvedValidatedAddress()
    {
        using var handler = (SocketsHttpHandler)AtprotoHardenedHttpClient.CreatePrimaryHandler(
            new AtprotoOutboundPolicy(false),
            TimeSpan.FromSeconds(1));
        await Assert.That(handler.AllowAutoRedirect).IsFalse();
        await Assert.That(handler.UseCookies).IsFalse();
        await Assert.That(handler.AutomaticDecompression).IsEqualTo(DecompressionMethods.None);

        IPAddress? connectedAddress = null;
        var expected = IPAddress.Parse("93.184.216.34");
        var stream = await AtprotoHardenedHttpClient.ConnectPinnedAsync(
            "issuer.example",
            443,
            new AtprotoOutboundPolicy(false),
            (_, _) => new([expected]),
            (address, _, _) =>
            {
                connectedAddress = address;
                return new ValueTask<Stream>(Stream.Null);
            },
            CancellationToken.None);

        await Assert.That(stream).IsSameReferenceAs(Stream.Null);
        await Assert.That(connectedAddress).IsEqualTo(expected);
    }

    [Test]
    public async Task BoundedHandlerPreservesHeadersDisposesOversizeAndDoesNotRelabelIoFailure()
    {
        var successContent = new TrackingByteArrayContent([1, 2, 3]);
        successContent.Headers.TryAddWithoutValidation("X-Content-Canary", "preserved");
        using (var successClient = new HttpClient(new AtprotoBoundedResponseHandler(
                   100,
                   new FixedResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = successContent
                   }))))
        using (var response = await successClient.GetAsync("https://example.com"))
        {
            await Assert.That(response.Content.Headers.GetValues("X-Content-Canary").Single()).IsEqualTo("preserved");
            await Assert.That((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
            await Assert.That(successContent.IsDisposed).IsTrue();
        }

        var oversized = new TrackingByteArrayContent(new byte[101]);
        using (var oversizedClient = new HttpClient(new AtprotoBoundedResponseHandler(
                   100,
                   new FixedResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = oversized
                   }))))
        {
            Func<Task> oversizedAct = () => oversizedClient.GetAsync("https://example.com");
            await Assert.That(oversizedAct).Throws<AtprotoOAuthSecurityException>();
            await Assert.That(oversized.IsDisposed).IsTrue();
        }

        using var ioClient = new HttpClient(new AtprotoBoundedResponseHandler(
            100,
            new FixedResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowingContent()
            })));
        Func<Task> ioAct = () => ioClient.GetAsync("https://example.com");
        var ioFailure = await Assert.That(ioAct).Throws<HttpRequestException>();
        await Assert.That(ioFailure).IsNotTypeOf<AtprotoOAuthSecurityException>();
        await Assert.That(ioFailure!.InnerException).IsTypeOf<IOException>();
    }

    [Test]
    public async Task FactoryReadinessRequiresStoresAndPinsRequestedKey()
    {
        var keyProvider = CreateKeyProvider(CreatePrivateJwks(("new", "active"), ("old", "retired")));
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var options = Options.Create(new AtprotoAuthenticationOptions
        {
            PublicUrl = "https://events.example.com/",
            CallbackPath = "/signin-atproto"
        });
        var unavailableServices = Substitute.For<IServiceProviderIsService>();
        using var unavailable = new AtprotoOAuthClientFactory(
            keyProvider,
            options,
            environment,
            unavailableServices,
            new AtprotoOAuthTransportFactory());
        await Assert.That(unavailable.GetReadiness().FailureCode).IsEqualTo("state_store_unavailable");

        var availableServices = Substitute.For<IServiceProviderIsService>();
        availableServices.IsService(typeof(IOAuthStateStore)).Returns(true);
        availableServices.IsService(typeof(IOAuthSessionStore)).Returns(true);
        using var ready = new AtprotoOAuthClientFactory(
            keyProvider,
            options,
            environment,
            availableServices,
            new AtprotoOAuthTransportFactory());
        await Assert.That(ready.GetReadiness().IsReady).IsTrue();
        var stateStore = new MemoryOAuthStateStore();
        var sessionStore = new MemoryOAuthSessionStore();
        using var current = ready.CreateForNewFlow(stateStore, sessionStore);
        await Assert.That(current.PinnedKeyId).IsEqualTo("new");
        var loggerFactory = typeof(OAuthSession)
            .GetField("_loggerFactory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(current.Session);
        await Assert.That(loggerFactory).IsSameReferenceAs(NullLoggerFactory.Instance);
        using var retired = ready.CreateForPinnedKey("old", stateStore, sessionStore);
        await Assert.That(retired.PinnedKeyId).IsEqualTo("old");
        Action unknown = () => ready.CreateForPinnedKey("missing", stateStore, sessionStore);
        await Assert.That(unknown).Throws<InvalidOperationException>();
        await Task.CompletedTask;
    }

    [Test]
    public async Task SingletonFactoryDoesNotCaptureScopedDurableStores()
    {
        var services = new ServiceCollection();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        services.AddSingleton(CreateKeyProvider());
        services.AddSingleton<IOptions<AtprotoAuthenticationOptions>>(Options.Create(new AtprotoAuthenticationOptions
        {
            PublicUrl = "https://events.example.com/",
            CallbackPath = "/signin-atproto"
        }));
        services.AddSingleton(environment);
        services.AddSingleton<IAtprotoOAuthTransportFactory, AtprotoOAuthTransportFactory>();
        services.AddScoped<IOAuthStateStore>(_ => new MemoryOAuthStateStore());
        services.AddScoped<IOAuthSessionStore>(_ => new MemoryOAuthSessionStore());
        services.AddSingleton<AtprotoOAuthClientFactory>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var factory = provider.GetRequiredService<AtprotoOAuthClientFactory>();
        await Assert.That(factory.GetReadiness().IsReady).IsTrue();
        using var scope = provider.CreateScope();
        using var lease = factory.CreateForNewFlow(
            scope.ServiceProvider.GetRequiredService<IOAuthStateStore>(),
            scope.ServiceProvider.GetRequiredService<IOAuthSessionStore>());
        await Assert.That(lease.PinnedKeyId).IsEqualTo("active");
        await Task.CompletedTask;
    }

    private static HttpClient CreateClient(
        AtprotoAuthorizationServerRegistry registry,
        AtprotoClientKeyProvider keyProvider,
        HttpMessageHandler inner,
        string keyId)
    {
        var policy = new AtprotoOutboundPolicy(false);
        var observation = new AtprotoAuthorizationServerMetadataHandler(registry, policy, inner);
        return new HttpClient(new AtprotoPrivateKeyJwtHandler(
            registry,
            keyProvider,
            ClientId,
            CallbackUri,
            Scope,
            keyId,
            observation));
    }

    private static AtprotoAuthorizationServerRegistry RegisteredRegistry()
        => RegisteredRegistry(Issuer);

    private static AtprotoAuthorizationServerRegistry RegisteredRegistry(string issuer)
    {
        var registry = new AtprotoAuthorizationServerRegistry();
        registry.Register(new AtprotoAuthorizationServerProfile(
            issuer,
            new Uri(issuer + "/oauth/par"),
            new Uri(issuer + "/oauth/token"),
            new Uri(issuer + "/oauth/revoke")));
        return registry;
    }

    private static FormUrlEncodedContent Form(string body) =>
        new(body.Split('&').Select(part => part.Split('=', 2)).Select(parts =>
            new KeyValuePair<string, string>(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts.ElementAtOrDefault(1) ?? string.Empty))));

    private static string ParForm() =>
        "client_id=" + Uri.EscapeDataString(ClientId)
        + "&redirect_uri=" + Uri.EscapeDataString(CallbackUri)
        + "&response_type=code&state=state&code_challenge=challenge&code_challenge_method=S256&scope="
        + Uri.EscapeDataString(Scope)
        + "&dpop_jkt=thumbprint";

    private static string CodeForm() =>
        "grant_type=authorization_code&code=code&code_verifier=verifier&redirect_uri="
        + Uri.EscapeDataString(CallbackUri)
        + "&client_id=" + Uri.EscapeDataString(ClientId);

    private static string RefreshForm() =>
        "grant_type=refresh_token&refresh_token=refresh&client_id=" + Uri.EscapeDataString(ClientId);

    private static async Task<HttpResponseMessage> SendFormAsync(HttpClient client, string endpoint, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = Form(body) };
        request.Headers.TryAddWithoutValidation("DPoP", "header.payload.signature");
        return await client.SendAsync(request);
    }

    private static HttpResponseMessage NonceResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("DPoP-Nonce", "nonce");
        return response;
    }

    private static HttpResponseMessage MetadataResponse(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return response;
    }

    private static string ValidMetadata(string tokenEndpoint = "https://issuer.example/oauth/token") => $$"""
        {"issuer":"{{Issuer}}","authorization_endpoint":"https://issuer.example/oauth/authorize","token_endpoint":"{{tokenEndpoint}}","pushed_authorization_request_endpoint":"https://issuer.example/oauth/par","revocation_endpoint":"https://issuer.example/oauth/revoke","require_pushed_authorization_requests":true,"token_endpoint_auth_methods_supported":["private_key_jwt"],"token_endpoint_auth_signing_alg_values_supported":["ES256"],"dpop_signing_alg_values_supported":["ES256"],"grant_types_supported":["authorization_code","refresh_token"],"response_types_supported":["code"],"code_challenge_methods_supported":["S256"],"authorization_response_iss_parameter_supported":true,"client_id_metadata_document_supported":true,"scopes_supported":["atproto"],"require_request_uri_registration":true}
        """;

    private static string MutateMetadata(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(ValidMetadata())!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }

    private static Dictionary<string, string> ParseForm(string body) => body.Split('&')
        .Select(part => part.Split('=', 2))
        .ToDictionary(parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')), parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')), StringComparer.Ordinal);

    private static JsonElement DecodeJwtPart(string jwt, int index)
    {
        var part = jwt.Split('.')[index].Replace('-', '+').Replace('_', '/');
        part += new string('=', (4 - part.Length % 4) % 4);
        return JsonDocument.Parse(Convert.FromBase64String(part)).RootElement.Clone();
    }

    private static async Task AssertAssertionBinding(string body, string issuer, string keyId)
    {
        var assertion = ParseForm(body)["client_assertion"];
        await Assert.That(DecodeJwtPart(assertion, 1).GetProperty("aud").GetString()).IsEqualTo(issuer);
        await Assert.That(DecodeJwtPart(assertion, 0).GetProperty("kid").GetString()).IsEqualTo(keyId);
    }

    private static AtprotoClientKeyProvider CreateKeyProvider(string? ring = null) =>
        new(Options.Create(new AtprotoClientKeyOptions { OAuthClientPrivateJwks = ring ?? CreatePrivateJwks(("active", "active")) }));

    private static string CreatePrivateJwks(params (string Kid, string Status)[] definitions)
    {
        var keys = new List<object>();
        foreach (var definition in definitions)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = key.ExportParameters(true);
            keys.Add(new { kty = "EC", crv = "P-256", x = Encode(parameters.Q.X!), y = Encode(parameters.Q.Y!), d = Encode(parameters.D!), kid = definition.Kid, use = "sig", alg = "ES256", status = definition.Status });
        }

        return JsonSerializer.Serialize(new { keys });
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class RecordingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int _attempt;
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return responseFactory(request, Interlocked.Increment(ref _attempt));
        }
    }

    private sealed class CarpaRetryHandler : HttpMessageHandler
    {
        private int _parAttempts;
        private int _tokenAttempts;
        public List<string> ParBodies { get; } = [];
        public List<string> TokenBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/.well-known/oauth-protected-resource")
            {
                return MetadataResponse("{\"authorization_servers\":[\"https://issuer.example\"]}");
            }

            if (request.Method == HttpMethod.Get && path == "/.well-known/oauth-authorization-server")
            {
                return MetadataResponse(ValidMetadata());
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (path == "/oauth/par")
            {
                ParBodies.Add(body);
                return RetryThenSuccess(
                    Interlocked.Increment(ref _parAttempts),
                    "{\"request_uri\":\"urn:ietf:params:oauth:request_uri:request\",\"expires_in\":90}");
            }

            if (path == "/oauth/token")
            {
                TokenBodies.Add(body);
                return RetryThenSuccess(
                    Interlocked.Increment(ref _tokenAttempts),
                    "{\"access_token\":\"access\",\"token_type\":\"DPoP\",\"expires_in\":3600,\"refresh_token\":\"refresh\",\"scope\":\"atproto transition:generic\",\"sub\":\"did:plc:user\"}");
            }

            throw new InvalidOperationException("Unexpected CarpaNet request path.");
        }

        private static HttpResponseMessage RetryThenSuccess(int attempt, string successBody)
        {
            var response = new HttpResponseMessage(attempt == 1 ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(
                    attempt == 1 ? "{\"error\":\"use_dpop_nonce\"}" : successBody,
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.TryAddWithoutValidation("DPoP-Nonce", "nonce-" + attempt);
            return response;
        }
    }

    private sealed class RestoredSessionHandler : HttpMessageHandler
    {
        public List<string> TokenBodies { get; } = [];
        public List<string> RevocationBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/.well-known/oauth-authorization-server")
            {
                return MetadataResponse(ValidMetadata());
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (path == "/oauth/token")
            {
                TokenBodies.Add(body);
                var response = MetadataResponse(
                    "{\"access_token\":\"refreshed-access\",\"token_type\":\"DPoP\",\"expires_in\":3600,\"refresh_token\":\"refresh-after-rotation\",\"scope\":\"atproto transition:generic\",\"sub\":\"did:plc:restored-user\"}");
                response.Headers.TryAddWithoutValidation("DPoP-Nonce", "refresh-nonce");
                return response;
            }

            if (path == "/oauth/revoke")
            {
                RevocationBodies.Add(body);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            throw new InvalidOperationException("Unexpected restored-session request path.");
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }

    private sealed class FixedResponseHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory());
    }

    private sealed class TrackingByteArrayContent(byte[] content) : ByteArrayContent(content)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new IOException("transport-canary"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

internal static class AtprotoOAuthTestAssertions
{
    public static async Task ShouldThrowAsync<TException>(this Func<Task> action) where TException : Exception =>
        await Assert.That(action).Throws<TException>();
}
