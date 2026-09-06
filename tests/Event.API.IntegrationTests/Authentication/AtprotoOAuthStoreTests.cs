// ABOUTME: Exercises protected OAuth state and tenant handoffs through the real API and PostgreSQL.
// ABOUTME: Proves browser and origin rejection precedes consumption across independent BFF adapters.

extern alias bff;

using System.Security.Cryptography;
using CarpaNet.OAuth.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using BffAuth = bff::Explore.Blazor.Services.Auth;
using BffOptions = bff::Explore.Blazor.Authentication;
using TenantConfiguration = Explore.Blazor.Client.Configuration.TenantConfiguration;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoTransientApiFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoOAuthStoreTests(AtprotoTransientApiFixture fixture)
{
    [Test]
    public async Task StateConsumeIsSingleUseAcrossAdaptersAndPopulatesExactFlowBinding()
    {
        await using var first = await CreateFixtureAsync();
        await using var second = await CreateFixtureAsync(first.Seed.TenantId, first.Protection, cookie: first.Cookie);
        string token = NewToken();
        var state = CreateState(first.Seed);
        await first.Store.StoreAsync(token, state);
        await Assert.That(await second.Store.GetPinnedKeyIdAsync(token)).IsEqualTo(first.Seed.OAuthClientKeyId);
        await Assert.That(await second.Store.GetPinnedKeyIdAsync(token)).IsEqualTo(first.Seed.OAuthClientKeyId);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<OAuthStateData?> Consume(BffAuth.ApiBackedOAuthStateStore store)
        {
            await start.Task.WaitAsync(deadline.Token);
            return await store.ConsumeAsync(token, deadline.Token);
        }
        Task<OAuthStateData?>[] contenders = [Consume(first.Store), Consume(second.Store)];
        start.SetResult();
        var results = await Task.WhenAll(contenders);

        await Assert.That(results.Count(result => result is not null)).IsEqualTo(1);
        var winner = first.Flow.Binding ?? second.Flow.Binding;
        await Assert.That(winner).IsNotNull();
        await Assert.That(winner!.Seed).IsEqualTo(first.Seed);
        await Assert.That(winner.Issuer.AbsoluteUri).IsEqualTo("https://issuer.example/");
        await Assert.That(results.Single(result => result is not null)!.Verifier).IsEqualTo(state.Verifier);
        await Assert.That(await first.Store.ConsumeAsync(token)).IsNull();
        await Assert.That(await second.Store.GetPinnedKeyIdAsync(token)).IsNull();
    }

    [Test]
    public async Task StateRoundTripsCanonicalActorTargetOnlyWhenTheCompletePairIsPresent()
    {
        await using var browser = await CreateFixtureAsync();
        var seed = browser.Seed with
        {
            CanonicalActorId = Guid.CreateVersion7(),
            ExpectedCanonicalActorConcurrencyStamp = Guid.CreateVersion7()
        };
        string token = NewToken();
        await browser.Store.StoreAsync(token, CreateState(seed));

        await Assert.That(await browser.Store.ConsumeAsync(token)).IsNotNull();
        await Assert.That(browser.Flow.Binding!.Seed.CanonicalActorId).IsEqualTo(seed.CanonicalActorId);
        await Assert.That(browser.Flow.Binding.Seed.ExpectedCanonicalActorConcurrencyStamp)
            .IsEqualTo(seed.ExpectedCanonicalActorConcurrencyStamp);
    }

    [Test]
    [Arguments("actor-only")]
    [Arguments("stamp-only")]
    [Arguments("empty-actor")]
    [Arguments("empty-stamp")]
    [Arguments("pds")]
    [Arguments("return-path")]
    [Arguments("tenant")]
    [Arguments("origin")]
    public async Task InvalidStateBindingIsRejectedBeforePersistence(string corruption)
    {
        await using var browser = await CreateFixtureAsync();
        var seed = corruption switch
        {
            "actor-only" => browser.Seed with { CanonicalActorId = Guid.CreateVersion7() },
            "stamp-only" => browser.Seed with { ExpectedCanonicalActorConcurrencyStamp = Guid.CreateVersion7() },
            "empty-actor" => browser.Seed with { CanonicalActorId = Guid.Empty, ExpectedCanonicalActorConcurrencyStamp = Guid.CreateVersion7() },
            "empty-stamp" => browser.Seed with { CanonicalActorId = Guid.CreateVersion7(), ExpectedCanonicalActorConcurrencyStamp = Guid.Empty },
            "pds" => browser.Seed with { ExpectedPdsUri = new("https://other-pds.example/") },
            "return-path" => browser.Seed with { ReturnPath = "//attacker.example/" },
            "tenant" => browser.Seed with { TenantId = Guid.Empty },
            "origin" => browser.Seed with { Origin = new("http://events.example.com/") },
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        string token = NewToken();

        await Assert.That(async () => await browser.Store.StoreAsync(token, CreateState(seed)))
            .Throws<InvalidOperationException>();
        await Assert.That(await browser.Transient.ReadAsync("oauth_state", token)).IsNull();
    }

    [Test]
    [Arguments("issuer")]
    [Arguments("host")]
    [Arguments("missing-proof")]
    [Arguments("wrong-proof")]
    public async Task RejectedCallbackLeavesStateAvailableToTheInitiatingBrowser(string rejection)
    {
        await using var owner = await CreateFixtureAsync();
        await using var attempt = await CreateFixtureAsync(owner.Seed.TenantId, owner.Protection, cookie: owner.Cookie);
        string token = NewToken();
        await owner.Store.StoreAsync(token, CreateState(owner.Seed));
        switch (rejection)
        {
            case "issuer": attempt.HttpContext.Request.QueryString = new("?iss=https%3A%2F%2Fattacker.example%2F"); break;
            case "host": attempt.HttpContext.Request.Host = new("other.example"); break;
            case "missing-proof": attempt.HttpContext.Request.Headers.Cookie = string.Empty; break;
            case "wrong-proof":
                var otherBrowser = CreateContext(owner.Seed.Origin.Host);
                _ = owner.Proof.CreateBinding(otherBrowser);
                attempt.HttpContext.Request.Headers.Cookie = IssuedCookie(otherBrowser);
                break;
        }

        await Assert.That(await attempt.Store.ConsumeAsync(token)).IsNull();
        await Assert.That(attempt.Flow.Binding).IsNull();
        await Assert.That(await owner.Store.ConsumeAsync(token)).IsNotNull();
        await Assert.That(owner.Flow.Binding!.Seed).IsEqualTo(owner.Seed);
        await Assert.That(await owner.Store.ConsumeAsync(token)).IsNull();
    }

    [Test]
    [Arguments("oauth_state")]
    [Arguments("tenant_handoff")]
    public async Task WrongDataProtectionKeyCannotDestroyTheLegitimateRecord(string purpose)
    {
        await using var owner = await CreateFixtureAsync();
        await using var wrongKey = await CreateFixtureAsync(owner.Seed.TenantId);
        wrongKey.HttpContext.Request.Headers.Cookie = owner.Cookie;
        if (purpose == "oauth_state")
        {
            string token = NewToken();
            await owner.Store.StoreAsync(token, CreateState(owner.Seed));
            await Assert.That(await wrongKey.Store.GetPinnedKeyIdAsync(token)).IsNull();
            await Assert.That(await wrongKey.Store.ConsumeAsync(token)).IsNull();
            await Assert.That(await owner.Store.ConsumeAsync(token)).IsNotNull();
        }
        else
        {
            string code = await owner.Handoffs.CreateAsync(owner.Seed, CreateSession(owner.Seed), default);
            await Assert.That(await wrongKey.Handoffs.ConsumeAsync(code, wrongKey.HttpContext.Request, default)).IsNull();
            await Assert.That(await owner.Handoffs.ConsumeAsync(code, owner.HttpContext.Request, default)).IsNotNull();
        }
    }

    [Test]
    public async Task CrossOriginCanonicalCallbackCanConsumeWithoutTheTenantHostCookie()
    {
        await using var browser = await CreateFixtureAsync(origin: "https://tenant.example/");
        browser.HttpContext.Request.Headers.Cookie = string.Empty;
        string token = NewToken();
        await browser.Store.StoreAsync(token, CreateState(browser.Seed));

        await Assert.That(await browser.Store.ConsumeAsync(token)).IsNotNull();
        await Assert.That(browser.Flow.Binding!.Seed.Origin.AbsoluteUri).IsEqualTo("https://tenant.example/");
        await Assert.That(browser.Flow.SessionResult).IsNull();
    }

    [Test]
    [Arguments("host")]
    [Arguments("tenant")]
    [Arguments("slug")]
    [Arguments("missing-proof")]
    [Arguments("wrong-proof")]
    public async Task RejectedHandoffLeavesCodeAvailableToTheInitiatingBrowser(string rejection)
    {
        await using var owner = await CreateFixtureAsync(origin: "https://tenant.example/");
        Guid destinationTenant = rejection == "tenant" ? await fixture.SeedTenantAsync() : owner.Seed.TenantId;
        await using var attempt = await CreateFixtureAsync(destinationTenant, owner.Protection,
            "https://tenant.example/", owner.Cookie, rejection == "slug" ? "other" : "default");
        owner.HttpContext.Request.Host = new("tenant.example");
        attempt.HttpContext.Request.Host = new(rejection == "host" ? "other.example" : "tenant.example");
        if (rejection == "missing-proof") attempt.HttpContext.Request.Headers.Cookie = string.Empty;
        if (rejection == "wrong-proof")
        {
            var otherBrowser = CreateContext("tenant.example");
            _ = owner.Proof.CreateBinding(otherBrowser);
            attempt.HttpContext.Request.Headers.Cookie = IssuedCookie(otherBrowser);
        }
        var session = CreateSession(owner.Seed);
        string code = await owner.Handoffs.CreateAsync(owner.Seed, session, default);

        await Assert.That(await attempt.Handoffs.ConsumeAsync(code, attempt.HttpContext.Request, default)).IsNull();
        var accepted = await owner.Handoffs.ConsumeAsync(code, owner.HttpContext.Request, default);
        await Assert.That(accepted).IsNotNull();
        await Assert.That(accepted!.Seed).IsEqualTo(owner.Seed);
        await Assert.That(accepted.Session).IsEqualTo(session);
        await Assert.That(await owner.Handoffs.ConsumeAsync(code, owner.HttpContext.Request, default)).IsNull();
    }

    [Test]
    public async Task HandoffPreservesCompleteCanonicalActorTargetPair()
    {
        await using var browser = await CreateFixtureAsync(origin: "https://tenant.example/");
        browser.HttpContext.Request.Host = new("tenant.example");
        var seed = browser.Seed with
        {
            CanonicalActorId = Guid.CreateVersion7(),
            ExpectedCanonicalActorConcurrencyStamp = Guid.CreateVersion7()
        };
        string code = await browser.Handoffs.CreateAsync(seed, CreateSession(seed), default);

        var handoff = await browser.Handoffs.ConsumeAsync(code, browser.HttpContext.Request, default);
        await Assert.That(handoff).IsNotNull();
        await Assert.That(handoff!.Seed.CanonicalActorId).IsEqualTo(seed.CanonicalActorId);
        await Assert.That(handoff.Session.ExpectedCanonicalActorConcurrencyStamp)
            .IsEqualTo(seed.ExpectedCanonicalActorConcurrencyStamp);
        await Assert.That(handoff.ExpiresAt).IsEqualTo(fixture.Clock.GetUtcNow().AddMinutes(2));
    }

    [Test]
    public async Task HandoffConsumptionHasExactlyOneWinnerAcrossIndependentAdapters()
    {
        await using var first = await CreateFixtureAsync();
        await using var second = await CreateFixtureAsync(first.Seed.TenantId, first.Protection, cookie: first.Cookie);
        string code = await first.Handoffs.CreateAsync(first.Seed, CreateSession(first.Seed), default);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<BffAuth.AtprotoTenantHandoff?> Consume(StoreFixture browser)
        {
            await start.Task.WaitAsync(deadline.Token);
            return await browser.Handoffs.ConsumeAsync(code, browser.HttpContext.Request, deadline.Token);
        }
        Task<BffAuth.AtprotoTenantHandoff?>[] contenders = [Consume(first), Consume(second)];
        start.SetResult();

        var results = await Task.WhenAll(contenders);
        await Assert.That(results.Count(result => result is not null)).IsEqualTo(1);
        await Assert.That(results.Single(result => result is not null)!.Seed).IsEqualTo(first.Seed);
    }

    [Test]
    [Arguments(4, 4)]
    [Arguments(60, 10)]
    public async Task StoredStateExpiryUsesTheSdkDeadlineBoundedByTenMinutes(int sdkMinutes, int expectedMinutes)
    {
        await using var browser = await CreateFixtureAsync();
        string token = NewToken();
        await browser.Store.StoreAsync(token, CreateState(browser.Seed, sdkMinutes));

        var candidate = await browser.Transient.ReadAsync("oauth_state", token);
        await Assert.That(candidate).IsNotNull();
        await Assert.That(candidate!.ExpiresAtUnixMilliseconds)
            .IsEqualTo(fixture.Clock.GetUtcNow().AddMinutes(expectedMinutes).ToUnixTimeMilliseconds());
        var state = await browser.Store.ConsumeAsync(token);
        await Assert.That(state).IsNotNull();
        await Assert.That(state!.ExpiresAt).IsEqualTo(fixture.Clock.GetUtcNow().AddMinutes(expectedMinutes));
    }

    private async Task<StoreFixture> CreateFixtureAsync(Guid? tenantId = null, IDataProtectionProvider? protection = null,
        string origin = "https://events.example.com/", string? cookie = null, string tenantSlug = "default")
    {
        Guid tenant = tenantId ?? await fixture.SeedTenantAsync();
        var services = await fixture.CreateBffServicesAsync();
        var transient = services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        protection ??= new EphemeralDataProtectionProvider();
        var proof = new BffAuth.AtprotoBrowserProof(protection, fixture.Clock);
        var challenge = CreateContext(new Uri(origin).Host, cookie);
        var binding = proof.CreateBinding(challenge);
        string browserCookie = cookie ?? IssuedCookie(challenge);
        var seed = new BffAuth.AtprotoOAuthFlowSeed("did:plc:alice", new("https://pds.example/"), tenant,
            tenantSlug, new(origin), "/events", "oauth-active", "person") { BrowserBinding = binding };
        var httpContext = CreateContext("events.example.com", browserCookie);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var flow = new BffAuth.AtprotoOAuthFlowContext();
        var resolver = CreateOriginResolver(tenant, tenantSlug, origin);
        var store = new BffAuth.ApiBackedOAuthStateStore(transient, protection, flow, accessor, resolver, proof, fixture.Clock);
        var handoffs = new BffAuth.AtprotoTenantSessionHandoffStore(transient, protection, resolver, proof, fixture.Clock);
        return new(services, transient, store, handoffs, flow, httpContext, proof, protection, seed, browserCookie);
    }

    private OAuthStateData CreateState(BffAuth.AtprotoOAuthFlowSeed seed, int sdkMinutes = 4) => new()
    {
        Issuer = "https://issuer.example/",
        PdsUrl = "https://pds.example/",
        AppState = BffAuth.ApiBackedOAuthStateStore.EncodeAppState(seed),
        Verifier = NewToken(),
        ExpiresAt = fixture.Clock.GetUtcNow().AddMinutes(sdkMinutes)
    };

    private BffAuth.AtprotoBffSessionResult CreateSession(BffAuth.AtprotoOAuthFlowSeed seed) => new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), seed.ExpectedDid, seed.Classification,
        NewToken(), fixture.Clock.GetUtcNow().AddMinutes(5), seed.CanonicalActorId, seed.ExpectedCanonicalActorConcurrencyStamp);

    private static BffAuth.AtprotoTenantOriginResolver CreateOriginResolver(Guid tenant, string slug, string origin)
    {
        var options = new BffOptions.AtprotoAuthenticationOptions
        {
            PublicUrl = "https://events.example.com/", CallbackPath = "/signin-atproto"
        };
        options.TenantOrigins.Add(new BffOptions.AtprotoTenantOrigin { Origin = origin, TenantId = tenant, TenantSlug = slug });
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        return new(Options.Create(options), Options.Create(new TenantConfiguration
        {
            DefaultTenantId = tenant, DefaultTenant = slug
        }), environment);
    }

    private static DefaultHttpContext CreateContext(string host, string? cookie = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new(host);
        context.Request.QueryString = new("?iss=https%3A%2F%2Fissuer.example%2F");
        if (cookie is not null) context.Request.Headers.Cookie = cookie;
        return context;
    }

    private static string IssuedCookie(HttpContext context) => context.Response.Headers.SetCookie.Single()!.Split(';', 2)[0];

    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private sealed record StoreFixture(ServiceProvider Services, BffAuth.ApiBackedAtprotoTransientStore Transient,
        BffAuth.ApiBackedOAuthStateStore Store, BffAuth.AtprotoTenantSessionHandoffStore Handoffs,
        BffAuth.AtprotoOAuthFlowContext Flow, DefaultHttpContext HttpContext, BffAuth.AtprotoBrowserProof Proof,
        IDataProtectionProvider Protection, BffAuth.AtprotoOAuthFlowSeed Seed, string Cookie) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
}
