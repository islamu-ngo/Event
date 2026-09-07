using CarpaNet.Identity;
using Explore.Blazor.Services.Auth;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class AtprotoIdentityCacheTests
{
    [Test]
    public async Task CacheKeepsHandleAndDidDocumentEntriesWithinTheirIndependentLimits()
    {
        using var cache = new AtprotoIdentityCache();
        const string initialDid = "did:plc:initial";

        await Assert.That(AtprotoIdentityCache.HandleTtl).IsEqualTo(TimeSpan.FromMinutes(2));
        await Assert.That(AtprotoIdentityCache.DidDocumentTtl).IsEqualTo(TimeSpan.FromMinutes(5));

        await cache.SetHandleDidAsync("Alice.Example", initialDid);
        await cache.SetDidDocumentAsync(initialDid, new DidDocument { Id = initialDid });

        await Assert.That((await cache.GetHandleDidAsync(" alice.example "))).IsEqualTo(initialDid);
        await Assert.That((await cache.GetDidDocumentAsync(" DID:PLC:INITIAL "))!.Id).IsEqualTo(initialDid);

        cache.Clear();
        for (var index = 0; index <= AtprotoIdentityCache.MaximumEntriesPerKind; index++)
        {
            var did = $"did:plc:{index:D4}";
            await cache.SetHandleDidAsync($"user-{index:D4}.example", did);
            await cache.SetDidDocumentAsync(did, new DidDocument { Id = did });
        }

        var retainedHandles = 0;
        var retainedDocuments = 0;
        for (var index = 0; index <= AtprotoIdentityCache.MaximumEntriesPerKind; index++)
        {
            if (await cache.GetHandleDidAsync($"user-{index:D4}.example") is not null)
            {
                retainedHandles++;
            }

            if (await cache.GetDidDocumentAsync($"did:plc:{index:D4}") is not null)
            {
                retainedDocuments++;
            }
        }

        await Assert.That(retainedHandles).IsEqualTo(AtprotoIdentityCache.MaximumEntriesPerKind);
        await Assert.That(retainedDocuments).IsEqualTo(AtprotoIdentityCache.MaximumEntriesPerKind);

        cache.RemoveHandle("user-0000.example");
        cache.RemoveDidDocument("did:plc:0000");
        await cache.SetHandleDidAsync("replacement.example", "did:plc:replacement");
        await cache.SetDidDocumentAsync(
            "did:plc:replacement",
            new DidDocument { Id = "did:plc:replacement" });

        await Assert.That((await cache.GetHandleDidAsync("replacement.example"))).IsEqualTo("did:plc:replacement");
        await Assert.That((await cache.GetDidDocumentAsync("did:plc:replacement"))!.Id).IsEqualTo("did:plc:replacement");
    }

    [Test]
    public async Task ExpiredHandleAndDidDocumentMappingsCanBeReplacedWithoutSleeping()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-22T00:00:00Z"));
        using var cache = new AtprotoIdentityCache(timeProvider);
        const string handle = "alice.example";
        const string firstDid = "did:plc:first";
        const string secondDid = "did:plc:second";

        await cache.SetHandleDidAsync(handle, firstDid);
        await cache.SetDidDocumentAsync(firstDid, new DidDocument { Id = firstDid });

        timeProvider.Advance(AtprotoIdentityCache.HandleTtl + TimeSpan.FromTicks(1));
        await Assert.That((await cache.GetHandleDidAsync(handle))).IsNull();
        await cache.SetHandleDidAsync(handle, secondDid);
        await Assert.That((await cache.GetHandleDidAsync(handle))).IsEqualTo(secondDid);

        timeProvider.Advance(
            AtprotoIdentityCache.DidDocumentTtl - AtprotoIdentityCache.HandleTtl);
        await Assert.That((await cache.GetDidDocumentAsync(firstDid))).IsNull();
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
