// ABOUTME: Attacks the private transient bridge through real HTTP, ES256 and PostgreSQL replay claims.
// ABOUTME: Proves missing authority fails closed and one assertion can dispatch at most once.

using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoTransientApiFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoTransientAuthenticationTests(AtprotoTransientApiFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublishedActiveAndRetiringKeys_AreTrustedForTheAssertionWindow(bool retiring)
    {
        byte[] body = fixture.ReadBody();
        using var request = fixture.Request(body, fixture.Sign(body, useRetiringKey: retiring));
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task MissingAssertion_IsGenericUnauthorizedWithoutTenantResolution()
    {
        using var request = fixture.Request(fixture.ReadBody(), null);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    [Arguments("iss")]
    [Arguments("aud")]
    [Arguments("sub")]
    [Arguments("use")]
    [Arguments("jti")]
    [Arguments("iat")]
    [Arguments("exp")]
    [Arguments("method")]
    [Arguments("path")]
    [Arguments("operation")]
    [Arguments("purpose")]
    [Arguments("body_sha256")]
    public async Task EverySecurityClaim_IsRequiredAndSingleton(string claim)
    {
        byte[] body = fixture.ReadBody();
        string missing = fixture.Sign(body, mutate: claims => claims.Remove(claim));
        string duplicate = fixture.Sign(body, payloadTransform: json =>
        {
            using var doc = JsonDocument.Parse(json);
            return json[..^1] + "," + JsonSerializer.Serialize(claim) + ":" + doc.RootElement.GetProperty(claim).GetRawText() + "}";
        });
        await RejectAsync(body, missing);
        await RejectAsync(body, duplicate);
    }

    [Test]
    [Arguments("issuer")]
    [Arguments("audience")]
    [Arguments("subject")]
    [Arguments("use")]
    [Arguments("method")]
    [Arguments("path")]
    [Arguments("operation")]
    [Arguments("purpose")]
    [Arguments("health-probe")]
    [Arguments("future")]
    [Arguments("stale")]
    [Arguments("expired")]
    [Arguments("long-life")]
    [Arguments("zero-life")]
    [Arguments("iat-string")]
    [Arguments("aud-array")]
    [Arguments("jti-empty")]
    [Arguments("body-digest")]
    [Arguments("bootstrap")]
    [Arguments("user-jwt")]
    public async Task ConfusedOrStaleAssertion_CannotConsumeStoredValue(string attack)
    {
        Guid tenant = await fixture.SeedTenantAsync();
        string digest = AtprotoTransientApiFixture.NewDigest();
        var row = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, digest, tenant,
            Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            fixture.Clock.GetUtcNow().AddMinutes(1).ToUnixTimeMilliseconds());
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAtprotoTransientStoreRepository>().TryCreateAsync(row);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { candidateId = row.Id, purpose = "oauth_state", tokenDigest = digest, expectedTenantId = tenant });
        long now = fixture.Clock.GetUtcNow().ToUnixTimeSeconds();
        string assertion = fixture.Sign(body, "consume", claims =>
        {
            switch (attack)
            {
                case "issuer": claims["iss"] = "other"; break;
                case "audience": claims["aud"] = "other"; break;
                case "subject": claims["sub"] = Guid.CreateVersion7().ToString(); break;
                case "use": claims["use"] = "session"; break;
                case "method": claims["method"] = "GET"; break;
                case "path": claims["path"] = AtprotoTransientApiFixture.Prefix + "read"; break;
                case "operation": claims["operation"] = "read"; break;
                case "purpose": claims["purpose"] = "tenant_handoff"; break;
                case "health-probe": claims["purpose"] = "health_probe"; break;
                case "future": claims["iat"] = now + 6; claims["exp"] = now + 30; break;
                case "stale": claims["iat"] = now - 36; claims["exp"] = now - 6; break;
                case "expired": claims["iat"] = now - 20; claims["exp"] = now - 5; break;
                case "long-life": claims["exp"] = now + 31; break;
                case "zero-life": claims["exp"] = now; break;
                case "iat-string": claims["iat"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case "aud-array": claims["aud"] = new[] { AtprotoTransientApiFixture.Audience }; break;
                case "jti-empty": claims["jti"] = Guid.Empty.ToString("D"); break;
                case "body-digest": claims["body_sha256"] = AtprotoTransientApiFixture.NewDigest(); break;
                case "bootstrap": claims["iss"] = Explore.API.Authentication.AtprotoJwtOptions.BootstrapIssuer; claims["aud"] = Explore.API.Authentication.AtprotoJwtOptions.BootstrapAudience; break;
                case "user-jwt": claims["iss"] = Explore.API.Authentication.AtprotoJwtOptions.SessionIssuer; claims["aud"] = Explore.API.Authentication.AtprotoJwtOptions.SessionAudience; break;
            }
        });
        await RejectAsync(body, assertion, "consume");
        await using var verifyScope = fixture.Factory.Services.CreateAsyncScope();
        var preserved = await verifyScope.ServiceProvider.GetRequiredService<IAtprotoTransientStoreRepository>().ReadOAuthStateAsync(digest);
        await Assert.That(preserved?.Id).IsEqualTo(row.Id);
    }

    [Test]
    [Arguments("alg", "none")]
    [Arguments("alg", "HS256")]
    [Arguments("kid", "unknown")]
    [Arguments("jku", "https://attacker.invalid/jwks")]
    [Arguments("x5u", "https://attacker.invalid/certificate")]
    [Arguments("typ", "other")]
    public async Task HeaderConfusionAndRemoteKeySources_AreRejected(string field, string value)
    {
        byte[] body = fixture.ReadBody();
        await RejectAsync(body, fixture.Sign(body, mutateHeader: header => header[field] = value));
    }

    [Test]
    public async Task DuplicateHeaderFieldsBodyFieldsAndTamperedBytes_AreRejected()
    {
        byte[] body = fixture.ReadBody();
        foreach (string field in new[] { "alg", "kid", "typ" })
            await RejectAsync(body, fixture.Sign(body, headerTransform: json =>
            {
                using var document = JsonDocument.Parse(json);
                return json[..^1] + "," + JsonSerializer.Serialize(field) + ":" + document.RootElement.GetProperty(field).GetRawText() + "}";
            }));
        byte[] duplicateBody = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body)[..^1] + ",\"Purpose\":\"oauth_state\"}");
        await RejectAsync(duplicateBody, fixture.Sign(duplicateBody));
        await RejectAsync([.. body, (byte)' '], fixture.Sign(body));
        string token = fixture.Sign(body);
        string[] parts = token.Split('.');
        byte[] signature = Base64UrlEncoder.DecodeBytes(parts[2]);
        signature[0] ^= 1;
        await RejectAsync(body, parts[0] + "." + parts[1] + "." + Base64UrlEncoder.Encode(signature));
    }

    [Test]
    [Arguments("Authorization")]
    [Arguments("X-API-Key")]
    [Arguments("X-Control-Plane-Key")]
    [Arguments("X-Setup-Secret")]
    [Arguments("X-Atproto-Bootstrap-Assertion")]
    [Arguments("X-Atproto-Session-Bridge-Assertion")]
    [Arguments("X-Test-Auth")]
    [Arguments("X-Atproto-Transient-Assertion")]
    public async Task ConflictingAndDuplicateCredentials_CannotClaimReplay(string header)
    {
        byte[] body = fixture.ReadBody();
        string token = fixture.Sign(body);
        using var request = fixture.Request(body, token);
        request.Headers.TryAddWithoutValidation(header, token);
        using var rejected = await fixture.Client.SendAsync(request);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var clean = fixture.Request(body, token);
        using var accepted = await fixture.Client.SendAsync(clean);
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments("read/")]
    [Arguments("READ")]
    [Arguments("read?unexpected=value")]
    public async Task NonCanonicalPath_CannotAuthenticate(string operation)
    {
        byte[] body = fixture.ReadBody();
        using var request = fixture.Request(body, fixture.Sign(body), operation);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task WrongMethod_CannotAuthenticate()
    {
        byte[] body = fixture.ReadBody();
        using var request = fixture.Request(body, fixture.Sign(body));
        request.Method = HttpMethod.Get;
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private async Task RejectAsync(byte[] body, string assertion, string operation = "read")
    {
        using var request = fixture.Request(body, assertion, operation);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        string output = await response.Content.ReadAsStringAsync();
        await Assert.That(output.Contains(assertion, StringComparison.Ordinal)).IsFalse();
        await Assert.That(output.Contains("tokenDigest", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task FastCleanupClock_CannotReopenReplayWithinMaximumReplicaDrift()
    {
        DateTimeOffset issuedAt = fixture.Clock.GetUtcNow();
        var cleanupClock = new AtprotoTransientApiFixture.FrozenClock { Now = issuedAt };
        string assertionId = Guid.CreateVersion7().ToString("D");
        byte[] body = fixture.ReadBody();
        string assertion = fixture.Sign(body, mutate: claims => claims["jti"] = assertionId);
        string digest = AtprotoTransientAssertionReplay.CreateFromAssertionId(assertionId,
            issuedAt.AddSeconds(35).ToUnixTimeMilliseconds()).AssertionDigest;
        try
        {
            using var firstRequest = fixture.Request(body, assertion);
            using var firstResponse = await fixture.Client.SendAsync(firstRequest);
            await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var cleanup = new AtprotoTransientCleanupService(new AtprotoTransientStoreRepository(context, cleanupClock),
                new AtprotoTransientAssertionReplayRepository(context, cleanupClock), cleanupClock);
            await Assert.That(await context.AtprotoTransientAssertionReplays.AsNoTracking()
                .Where(row => row.AssertionDigest == digest).Select(row => row.ExpiresAtUnixMilliseconds).SingleAsync())
                .IsEqualTo(issuedAt.AddSeconds(35).ToUnixTimeMilliseconds());

            // Each host can differ from trusted UTC by five seconds, giving a ten-second pairwise spread.
            cleanupClock.Now = issuedAt.AddSeconds(35);
            fixture.Clock.Now = issuedAt.AddSeconds(25);
            await cleanup.CleanupExpiredAsync();
            await RejectAsync(body, assertion);

            cleanupClock.Now = issuedAt.AddMilliseconds(44_999);
            fixture.Clock.Now = issuedAt.AddMilliseconds(34_999);
            await cleanup.CleanupExpiredAsync();
            await RejectAsync(body, assertion);
            await Assert.That(await context.AtprotoTransientAssertionReplays.AsNoTracking()
                .Where(row => row.AssertionDigest == digest).Select(row => row.ExpiresAtUnixMilliseconds).SingleAsync())
                .IsEqualTo(issuedAt.AddSeconds(35).ToUnixTimeMilliseconds());

            // A distinct assertion of the same age can still dispatch on the slow verifier.
            string unclaimed = fixture.Sign(body, mutate: claims =>
            {
                claims["iat"] = issuedAt.ToUnixTimeSeconds();
                claims["exp"] = issuedAt.AddSeconds(30).ToUnixTimeSeconds();
            });
            using var liveRequest = fixture.Request(body, unclaimed);
            using var liveResponse = await fixture.Client.SendAsync(liveRequest);
            await Assert.That(liveResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            cleanupClock.Now = issuedAt.AddSeconds(45);
            fixture.Clock.Now = issuedAt.AddSeconds(35);
            await cleanup.CleanupExpiredAsync();
            await Assert.That(await context.AtprotoTransientAssertionReplays.AnyAsync(row => row.AssertionDigest == digest)).IsFalse();
            await RejectAsync(body, assertion);
        }
        finally
        {
            fixture.Clock.Now = issuedAt;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReplayInsertCommittingAtExpiry_CannotDispatchEvenAfterCleanup(bool previouslyClaimed)
    {
        DateTimeOffset issuedAt = fixture.Clock.GetUtcNow();
        byte[] body = fixture.ReadBody();
        string assertionId = Guid.CreateVersion7().ToString("D");
        string assertion = fixture.Sign(body, mutate: claims => claims["jti"] = assertionId);
        string digest = AtprotoTransientAssertionReplay.CreateFromAssertionId(assertionId,
            issuedAt.AddSeconds(35).ToUnixTimeMilliseconds()).AssertionDigest;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpStatusCode>? pending = null;
        try
        {
            if (previouslyClaimed)
                await Assert.That(await SendAsync()).IsEqualTo(HttpStatusCode.NotFound);
            fixture.Clock.Now = issuedAt.AddMilliseconds(34_999);
            int blockNext = 1;
            fixture.BeforeReplayInsert = async cancellationToken =>
            {
                if (Interlocked.Exchange(ref blockNext, 0) == 0) return;
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            };
            pending = SendAsync();
            await entered.Task.WaitAsync(deadline.Token);

            fixture.Clock.Now = issuedAt.AddSeconds(35);
            var cleanupClock = new AtprotoTransientApiFixture.FrozenClock { Now = issuedAt.AddSeconds(45) };
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That(await context.AtprotoTransientAssertionReplays.AnyAsync(row => row.AssertionDigest == digest))
                .IsEqualTo(previouslyClaimed);
            var cleanup = new AtprotoTransientCleanupService(new AtprotoTransientStoreRepository(context, cleanupClock),
                new AtprotoTransientAssertionReplayRepository(context, cleanupClock), cleanupClock);
            await cleanup.CleanupExpiredAsync(deadline.Token);
            await Assert.That(await context.AtprotoTransientAssertionReplays.AnyAsync(row => row.AssertionDigest == digest)).IsFalse();

            release.TrySetResult();
            await Assert.That(await pending).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(await context.AtprotoTransientAssertionReplays.AsNoTracking()
                .Where(row => row.AssertionDigest == digest).Select(row => row.ExpiresAtUnixMilliseconds).SingleAsync())
                .IsEqualTo(issuedAt.AddSeconds(35).ToUnixTimeMilliseconds());
            await RejectAsync(body, assertion);
        }
        finally
        {
            release.TrySetResult();
            fixture.BeforeReplayInsert = null;
            try { if (pending is not null) await pending; }
            finally { fixture.Clock.Now = issuedAt; }
        }

        async Task<HttpStatusCode> SendAsync()
        {
            using var request = fixture.Request(body, assertion);
            using var response = await fixture.Client.SendAsync(request, deadline.Token);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            return response.StatusCode;
        }
    }

    [Test]
    public async Task ConcurrentReplay_DispatchesExactlyOnce()
    {
        byte[] body = fixture.ReadBody();
        string assertion = fixture.Sign(body);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<HttpStatusCode>[] attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            using var request = fixture.Request(body, assertion);
            await start.Task.WaitAsync(deadline.Token);
            using var response = await fixture.Client.SendAsync(request, deadline.Token);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            return response.StatusCode;
        }).ToArray();
        start.SetResult();
        HttpStatusCode[] statuses = await Task.WhenAll(attempts);
        await Assert.That(statuses.Count(status => status == HttpStatusCode.NotFound)).IsEqualTo(1);
        await Assert.That(statuses.Count(status => status == HttpStatusCode.Unauthorized)).IsEqualTo(1);
    }
}
