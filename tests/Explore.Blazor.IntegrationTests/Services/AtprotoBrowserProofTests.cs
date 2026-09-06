// ABOUTME: Exercises real Data Protection, response cookie serialization and independent browser cookie jars.
// ABOUTME: Guards stable parallel-flow binding, expiry budgets, cold-cookie races and cross-origin/key-loss rejection.

using System.Net;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class AtprotoBrowserProofTests
{
    private static readonly Uri Origin = new("https://events.example.test");

    [Test]
    public async Task FirstChallenge_IssuesOneBoundedHostOnlyProtectedCookie()
    {
        var clock = new Clock();
        var proof = new AtprotoBrowserProof(new EphemeralDataProtectionProvider(), clock);
        var context = Request(new CookieContainer());
        var binding = proof.CreateBinding(context);
        string value = context.Response.Headers.SetCookie.Single()!;
        var cookie = SetCookieHeaderValue.Parse(value);
        await Assert.That(cookie.Name.Value).IsEqualTo(AtprotoBrowserProof.CookieName);
        await Assert.That(cookie.Value.Length).IsLessThan(1024);
        await Assert.That(cookie.Secure && cookie.HttpOnly).IsTrue();
        await Assert.That(cookie.SameSite).IsEqualTo(Microsoft.Net.Http.Headers.SameSiteMode.Lax);
        await Assert.That(cookie.Path.Value).IsEqualTo("/");
        await Assert.That(cookie.Domain.HasValue).IsFalse();
        await Assert.That(cookie.MaxAge).IsEqualTo(TimeSpan.FromMinutes(15));
        await Assert.That(cookie.Expires).IsEqualTo(clock.GetUtcNow().AddMinutes(15));
        await Assert.That(binding.ProofExpiresAt).IsEqualTo(clock.GetUtcNow().AddMinutes(15));
    }

    [Test]
    public async Task EstablishedCookie_IsReusedAcrossReplicasAndOutOfOrderFlowsWithoutRewriting()
    {
        var clock = new Clock();
        var protection = new EphemeralDataProtectionProvider();
        var first = new AtprotoBrowserProof(protection, clock);
        var second = new AtprotoBrowserProof(protection, clock);
        var jar = new CookieContainer();
        var start = Request(jar);
        _ = first.CreateBinding(start);
        StoreCookie(jar, start);
        string original = jar.GetCookieHeader(Origin);
        BffAtprotoBrowserBinding[] bindings = await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(() =>
        {
            var request = Request(jar);
            var binding = (index % 2 == 0 ? first : second).CreateBinding(request);
            if (request.Response.Headers.SetCookie.Count != 0) throw new InvalidOperationException("Existing proof was rewritten.");
            return binding;
        })));
        await Assert.That(bindings.Select(binding => binding.FlowId).Distinct().Count()).IsEqualTo(20);
        await Assert.That(bindings.Select(binding => binding.ProofDigest).Distinct().Count()).IsEqualTo(20);
        foreach (var binding in bindings.Reverse())
            await Assert.That(second.Validate(Request(jar).Request, binding)).IsTrue();
        await Assert.That(jar.GetCookieHeader(Origin)).IsEqualTo(original);
    }

    [Test]
    public async Task MissingWrongAndAlteredProof_CannotCompleteAnotherBrowsersFlow()
    {
        var proof = new AtprotoBrowserProof(new EphemeralDataProtectionProvider(), new Clock());
        var legitimate = new CookieContainer();
        var start = Request(legitimate);
        var binding = proof.CreateBinding(start);
        StoreCookie(legitimate, start);
        var other = new CookieContainer();
        await Assert.That(proof.Validate(Request(other).Request, binding)).IsFalse();
        var otherStart = Request(other);
        var otherBinding = proof.CreateBinding(otherStart);
        StoreCookie(other, otherStart);
        await Assert.That(proof.Validate(Request(other).Request, binding)).IsFalse();
        await Assert.That(proof.Validate(Request(legitimate).Request, binding with { FlowId = otherBinding.FlowId })).IsFalse();
        await Assert.That(proof.Validate(Request(legitimate).Request, binding with { ProofDigest = otherBinding.ProofDigest })).IsFalse();
        await Assert.That(proof.Validate(Request(legitimate).Request, binding with { ProofExpiresAt = binding.ProofExpiresAt.AddSeconds(1) })).IsFalse();
        await Assert.That(proof.Validate(Request(legitimate).Request, binding)).IsTrue();
    }

    [Test]
    public async Task PersistedProtectionKeys_SurviveRestartAndRotationWithoutRewritingEstablishedProof()
    {
        var directory = Directory.CreateTempSubdirectory("atproto-proof-keys-");
        var clock = new Clock();
        var jar = new CookieContainer();
        ServiceProvider CreateProvider(string application = "islamu-event")
        {
            var services = new ServiceCollection();
            services.AddDataProtection().PersistKeysToFileSystem(directory).SetApplicationName(application);
            return services.BuildServiceProvider();
        }
        AtprotoBrowserProof Proof(ServiceProvider services) =>
            new(services.GetRequiredService<IDataProtectionProvider>(), clock);
        try
        {
            BffAtprotoBrowserBinding binding;
            await using (var first = CreateProvider())
            {
                var start = Request(jar);
                binding = Proof(first).CreateBinding(start);
                StoreCookie(jar, start);
            }
            string originalCookie = jar.GetCookieHeader(Origin);
            await using (var restarted = CreateProvider())
            {
                await Assert.That(Proof(restarted).Validate(Request(jar).Request, binding)).IsTrue();
                restarted.GetRequiredService<IKeyManager>().CreateNewKey(
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
            }
            await using var rotated = CreateProvider();
            var established = Request(jar);
            var nextBinding = Proof(rotated).CreateBinding(established);
            await Assert.That(established.Response.Headers.SetCookie.Count).IsEqualTo(0);
            await Assert.That(Proof(rotated).Validate(Request(jar).Request, binding)).IsTrue();
            await Assert.That(Proof(rotated).Validate(Request(jar).Request, nextBinding)).IsTrue();
            await Assert.That(nextBinding.FlowId).IsNotEqualTo(binding.FlowId);
            await Assert.That(nextBinding.ProofExpiresAt).IsEqualTo(binding.ProofExpiresAt);
            await Assert.That(jar.GetCookieHeader(Origin)).IsEqualTo(originalCookie);

            var newBrowser = new CookieContainer();
            var newStart = Request(newBrowser);
            var newBinding = Proof(rotated).CreateBinding(newStart);
            StoreCookie(newBrowser, newStart);
            await using var replica = CreateProvider();
            await Assert.That(Proof(replica).Validate(Request(newBrowser).Request, newBinding)).IsTrue();
            await using var wrongApplication = CreateProvider("different-application");
            await Assert.That(Proof(wrongApplication).Validate(Request(jar).Request, binding)).IsFalse();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Test]
    public async Task CrossOriginTransplantAndLostProtectionKeys_FailClosed()
    {
        var clock = new Clock();
        var proof = new AtprotoBrowserProof(new EphemeralDataProtectionProvider(), clock);
        var jar = new CookieContainer();
        var start = Request(jar);
        var binding = proof.CreateBinding(start);
        StoreCookie(jar, start);
        var foreign = new Uri("https://unrelated.example.test");
        await Assert.That(jar.GetCookieHeader(foreign)).IsEqualTo(string.Empty);
        var transplant = Request(jar, foreign);
        transplant.Request.Headers.Cookie = jar.GetCookieHeader(Origin);
        await Assert.That(proof.Validate(transplant.Request, binding)).IsFalse();
        var restarted = new AtprotoBrowserProof(new EphemeralDataProtectionProvider(), clock);
        await Assert.That(restarted.Validate(Request(jar).Request, binding)).IsFalse();
    }

    [Test]
    public async Task NearExpiryStart_DoesNotRotateProofOrInvalidateExistingFlows()
    {
        var clock = new Clock();
        var proof = new AtprotoBrowserProof(new EphemeralDataProtectionProvider(), clock);
        var jar = new CookieContainer();
        var start = Request(jar);
        var binding = proof.CreateBinding(start);
        StoreCookie(jar, start);
        clock.Advance(TimeSpan.FromMinutes(13.5));
        var near = Request(jar);
        AtprotoProofExpiryException? failure = null;
        try { proof.CreateBinding(near); }
        catch (AtprotoProofExpiryException exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.RetryAfterSeconds).IsEqualTo(90);
        await Assert.That(near.Response.Headers.SetCookie.Count).IsEqualTo(0);
        await Assert.That(proof.Validate(Request(jar).Request, binding)).IsTrue();
        clock.Advance(TimeSpan.FromSeconds(90));
        await Assert.That(proof.Validate(Request(jar).Request, binding)).IsFalse();
        var renewed = Request(jar);
        var next = proof.CreateBinding(renewed);
        StoreCookie(jar, renewed);
        await Assert.That(next.ProofExpiresAt).IsEqualTo(clock.GetUtcNow().AddMinutes(15));
        await Assert.That(proof.Validate(Request(jar).Request, binding)).IsFalse();
        await Assert.That(proof.Validate(Request(jar).Request, next)).IsTrue();
    }

    [Test]
    public async Task StateAndHandoff_UseIndependentBudgetsWithinTheFixedProofDeadline()
    {
        var clock = new Clock();
        var proof = new AtprotoBrowserProof(new EphemeralDataProtectionProvider(), clock);
        var binding = proof.CreateBinding(Request(new CookieContainer()));
        var initial = clock.GetUtcNow();
        await Assert.That(proof.StateExpiry(binding, initial.AddMinutes(5))).IsEqualTo(initial.AddMinutes(5));
        await Assert.That(proof.StateExpiry(binding, initial.AddMinutes(30))).IsEqualTo(initial.AddMinutes(10));
        clock.Advance(TimeSpan.FromMinutes(12));
        var stateExpiry = proof.StateExpiry(binding, clock.GetUtcNow().AddMinutes(5));
        await Assert.That(stateExpiry).IsEqualTo(initial.AddMinutes(13));
        clock.Advance(TimeSpan.FromSeconds(30));
        await Assert.That(proof.HandoffExpiry(binding)).IsEqualTo(initial.AddMinutes(14.5));
        clock.Advance(TimeSpan.FromMinutes(1));
        await Assert.That(proof.HandoffExpiry(binding)).IsEqualTo(initial.AddMinutes(15));
    }

    [Test]
    public async Task ConcurrentFirstCookies_AllowOnlyTheBindingForTheCookieTheBrowserKeeps()
    {
        var proof = new AtprotoBrowserProof(new EphemeralDataProtectionProvider(), new Clock());
        var jar = new CookieContainer();
        var first = Request(jar);
        var second = Request(jar);
        var losingBinding = proof.CreateBinding(first);
        var winningBinding = proof.CreateBinding(second);
        StoreCookie(jar, first);
        StoreCookie(jar, second);
        await Assert.That(proof.Validate(Request(jar).Request, losingBinding)).IsFalse();
        await Assert.That(proof.Validate(Request(jar).Request, winningBinding)).IsTrue();
    }

    [Test]
    public async Task InsecureMalformedAndDuplicateCookies_CannotEstablishProof()
    {
        var proof = new AtprotoBrowserProof(new EphemeralDataProtectionProvider(), new Clock());
        var insecure = Request(new CookieContainer(), new Uri("http://localhost"));
        await Assert.That(() => proof.CreateBinding(insecure)).Throws<InvalidOperationException>();
        await Assert.That(insecure.Response.Headers.SetCookie.Count).IsEqualTo(0);
        var malformed = Request(new CookieContainer());
        malformed.Request.Headers.Cookie = AtprotoBrowserProof.CookieName + "=invalid";
        await Assert.That(() => proof.CreateBinding(malformed)).Throws<InvalidOperationException>();
        var start = Request(new CookieContainer());
        var binding = proof.CreateBinding(start);
        var jar = new CookieContainer();
        StoreCookie(jar, start);
        var duplicate = Request(jar);
        duplicate.Request.Headers.Cookie = jar.GetCookieHeader(Origin) + "; " + jar.GetCookieHeader(Origin);
        await Assert.That(proof.Validate(duplicate.Request, binding)).IsFalse();
        await Assert.That(() => proof.CreateBinding(duplicate)).Throws<InvalidOperationException>();
    }

    private static DefaultHttpContext Request(CookieContainer jar, Uri? origin = null)
    {
        origin ??= Origin;
        var context = new DefaultHttpContext();
        context.Request.Scheme = origin.Scheme;
        context.Request.Host = new HostString(origin.Authority);
        context.Request.Headers.Cookie = jar.GetCookieHeader(origin);
        return context;
    }

    private static void StoreCookie(CookieContainer jar, HttpContext context)
    {
        foreach (string? header in context.Response.Headers.SetCookie)
            jar.SetCookies(Origin, header!);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }
}
