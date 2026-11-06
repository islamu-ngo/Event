using Explore.Domain;

namespace Event.Domain.UnitTests.Authentication;

public sealed class TenantAuthHandoffTests
{
    private const long Created = 1_900_000_000_000;
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task TwoMinuteGrantKeepsExactDestinationPortAndOriginalBindings()
    {
        var binding = Binding();

        var grant = TenantAuthHandoff.Create(binding, Digest, "protected-ticket", Created, Created + 120_000);

        await Assert.That(grant.OwnerUserId).IsEqualTo(binding.OwnerUserId);
        await Assert.That(grant.TenantId).IsEqualTo(binding.TenantId);
        await Assert.That(grant.DestinationOrigin).IsEqualTo("https://tenant.example.test:8443/");
        await Assert.That(grant.LocalPath).IsEqualTo("/events?view=upcoming");
        await Assert.That(grant.BrowserProofDigest).IsEqualTo(binding.BrowserProofDigest);
        await Assert.That(grant.SourceRevisionDigest).IsEqualTo(binding.SourceRevisionDigest);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(120_001)]
    public async Task GrantCannotOutliveItsBoundedWindowOrExpireBeforeCreation(long lifetime)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(
            TenantAuthHandoff.Create(Binding(), Digest, "protected-ticket", Created, Created + lifetime)));
    }

    [Test]
    [Arguments("owner")]
    [Arguments("tenant")]
    [Arguments("flow")]
    [Arguments("session")]
    [Arguments("revision")]
    [Arguments("browser")]
    [Arguments("routing")]
    public async Task GrantRequiresEveryOriginalIdentityAndDigestBinding(string missing)
    {
        var binding = missing switch
        {
            "owner" => Binding() with { OwnerUserId = Guid.Empty },
            "tenant" => Binding() with { TenantId = Guid.Empty },
            "flow" => Binding() with { FlowId = Guid.Empty },
            "session" => Binding() with { SourceSessionDigest = string.Empty },
            "revision" => Binding() with { SourceRevisionDigest = new string('A', 64) },
            "browser" => Binding() with { BrowserProofDigest = string.Empty },
            "routing" => Binding() with { RoutingDigest = string.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(missing))
        };

        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(
            TenantAuthHandoff.Create(binding, Digest, "protected-ticket", Created, Created + 1)));
    }

    [Test]
    [Arguments("http://tenant.example.test/")]
    [Arguments("https://user@tenant.example.test/")]
    [Arguments("https://tenant.example.test/path")]
    [Arguments("https://tenant.example.test/?proof=unexpected")]
    [Arguments("https://tenant.example.test/#fragment")]
    [Arguments("https://source.example.test:443/")]
    public async Task GrantRequiresASeparateExactHttpsOrigin(string destination)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(TenantAuthHandoff.Create(
            Binding() with { DestinationOrigin = destination }, Digest, "protected-ticket", Created, Created + 1)));
    }

    [Test]
    [Arguments("")]
    [Arguments("//other.example.test/events")]
    [Arguments("/\\other.example.test/events")]
    [Arguments("/%2fother.example.test/events")]
    [Arguments("/%5cother.example.test/events")]
    [Arguments("/events\r\n")]
    public async Task GrantRejectsNonlocalOrAmbiguousDestinationPaths(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(TenantAuthHandoff.Create(
            Binding() with { LocalPath = path }, Digest, "protected-ticket", Created, Created + 1)));
    }

    [Test]
    public async Task ProtectedTicketLimitCountsEncodedBytes()
    {
        string ciphertext = new('\u00e9', TenantAuthHandoff.MaximumProtectedTicketBytes / 2 + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(
            TenantAuthHandoff.Create(Binding(), Digest, ciphertext, Created, Created + 1)));
    }

    private static TenantAuthHandoffBinding Binding() => new()
    {
        OwnerUserId = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), FlowId = Guid.CreateVersion7(),
        SourceSessionDigest = Digest, SourceRevisionDigest = Digest, BrowserProofDigest = Digest,
        RoutingDigest = Digest, SourceOrigin = "https://source.example.test/",
        DestinationOrigin = "https://tenant.example.test:8443/", LocalPath = "/events?view=upcoming"
    };
}
