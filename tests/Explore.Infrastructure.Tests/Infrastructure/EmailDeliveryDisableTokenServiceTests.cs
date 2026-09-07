// ABOUTME: Exercises disable-preview tokens with real Data Protection and immutable impact snapshots.
// ABOUTME: Rejects stale scope bindings, malformed input, tampering, purpose confusion, and expired previews.

using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Infrastructure.Mail;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class EmailDeliveryDisableTokenServiceTests
{
    [Test]
    public async Task Matches_RequiresSameActorAndEveryImpactField()
    {
        var service = new EmailDeliveryDisableTokenService(new EphemeralDataProtectionProvider());
        var actor = Guid.NewGuid();
        var snapshot = CreateSnapshot();
        var issued = service.Issue(actor, snapshot);

        await Assert.That(service.Matches(issued.Token, actor, snapshot)).IsTrue();
        await Assert.That(service.Matches(issued.Token, Guid.NewGuid(), snapshot)).IsFalse();
        await Assert.That(service.Matches(issued.Token, Guid.Empty, snapshot)).IsFalse();

        EmailDeliveryDisableImpactSnapshot[] changed =
        [
            snapshot with { TenantId = Guid.NewGuid() },
            snapshot with { Revision = snapshot.Revision + 1 },
            snapshot with { IsLocked = true },
            snapshot with { AffectedScopes = snapshot.AffectedScopes.RemoveAt(1) },
            snapshot with { AffectedScopes = snapshot.AffectedScopes.Add(new(TenantId: Guid.NewGuid(), Revision: 0)) },
            snapshot with { AffectedScopes = snapshot.AffectedScopes.SetItem(1, new(TenantId: Guid.NewGuid(), Revision: 8)) },
            snapshot with { AffectedScopes = snapshot.AffectedScopes.SetItem(1, snapshot.AffectedScopes[1] with { Revision = 9 }) }
        ];

        foreach (var candidate in changed)
        {
            await Assert.That(service.Matches(issued.Token, actor, candidate)).IsFalse();
        }
    }

    [Test]
    public async Task Matches_BindsTenantSelectionAndCanonicalizesLargeAffectedSet()
    {
        var service = new EmailDeliveryDisableTokenService(new EphemeralDataProtectionProvider());
        var actor = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var tenantSnapshot = new EmailDeliveryDisableImpactSnapshot(
            TenantId: tenantId,
            Revision: 12,
            IsLocked: false,
            AffectedScopes: [new(TenantId: tenantId, Revision: 12)]);
        var tenantToken = service.Issue(actor, tenantSnapshot);
        await Assert.That(service.Matches(tenantToken.Token, actor, tenantSnapshot)).IsTrue();
        await Assert.That(service.Matches(tenantToken.Token, actor, tenantSnapshot with { TenantId = null })).IsFalse();

        var snapshot = CreateSnapshot() with
        {
            AffectedScopes = Enumerable.Range(0, 5_000)
                .Select(revision => new EmailDeliveryAffectedScope(TenantId: Guid.NewGuid(), Revision: revision))
                .ToImmutableArray()
        };
        var issued = service.Issue(actor, snapshot);
        var reordered = snapshot with { AffectedScopes = snapshot.AffectedScopes.Reverse().ToImmutableArray() };

        await Assert.That(issued.Token.Length <= 2048).IsTrue();
        await Assert.That(issued.Token.Length).IsEqualTo(tenantToken.Token.Length);
        await Assert.That(service.Matches(issued.Token, actor, reordered)).IsTrue();
    }

    [Test]
    public async Task InvalidSnapshots_CannotBeIssuedOrValidated()
    {
        var service = new EmailDeliveryDisableTokenService(new EphemeralDataProtectionProvider());
        var actor = Guid.NewGuid();
        var snapshot = CreateSnapshot();
        var token = service.Issue(actor, snapshot).Token;
        EmailDeliveryDisableImpactSnapshot[] invalid =
        [
            null!,
            snapshot with { TenantId = Guid.Empty },
            snapshot with { Revision = -1 },
            snapshot with { IsLocked = true },
            snapshot with { AffectedScopes = default },
            snapshot with { AffectedScopes = [] },
            snapshot with { AffectedScopes = [null!] },
            snapshot with { AffectedScopes = [new(TenantId: Guid.Empty, Revision: 0)] },
            snapshot with { AffectedScopes = [new(TenantId: null, Revision: -1)] },
            snapshot with { AffectedScopes = snapshot.AffectedScopes.Add(snapshot.AffectedScopes[1] with { Revision = 9 }) },
            snapshot with { AffectedScopes = [new(TenantId: null, Revision: 0), new(TenantId: null, Revision: 1)] }
        ];

        foreach (var candidate in invalid)
        {
            await Assert.That(() => service.Issue(actor, candidate)).Throws<ArgumentException>();
            await Assert.That(service.Matches(token, actor, candidate)).IsFalse();
        }

        await Assert.That(() => service.Issue(Guid.Empty, snapshot)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Matches_RejectsMalformedTamperedAndOtherPurposeTokens()
    {
        var provider = new EphemeralDataProtectionProvider();
        var service = new EmailDeliveryDisableTokenService(provider);
        var actor = Guid.NewGuid();
        var snapshot = CreateSnapshot();
        var token = service.Issue(actor, snapshot).Token;
        var middle = token.Length / 2;
        var tampered = token[..middle] + (token[middle] == 'a' ? 'b' : 'a') + token[(middle + 1)..];
        var otherPurpose = new EmailDeliveryDisableTokenService(provider.CreateProtector("OtherOperation"));
        var foreignToken = otherPurpose.Issue(actor, snapshot).Token;

        foreach (var candidate in new string?[] { null, "", " ", "@@@", "a", "invalid-token", new('a', 2049), tampered, foreignToken })
        {
            await Assert.That(service.Matches(candidate, actor, snapshot)).IsFalse();
        }

        await Assert.That(otherPurpose.Matches(token, actor, snapshot)).IsFalse();
        await Assert.That(new EmailDeliveryDisableTokenService(new EphemeralDataProtectionProvider())
            .Matches(token, actor, snapshot)).IsFalse();
    }

    [Test]
    public async Task Matches_RejectsNativeExpiredTokenWithoutSleeping()
    {
        var now = DateTimeOffset.UtcNow.AddHours(-1);
        var provider = new EphemeralDataProtectionProvider();
        var issuingService = new EmailDeliveryDisableTokenService(provider, new FixedTimeProvider(now));
        var validatingService = new EmailDeliveryDisableTokenService(provider);
        var actor = Guid.NewGuid();
        var snapshot = CreateSnapshot();

        var issued = issuingService.Issue(actor, snapshot);

        await Assert.That(issued.ExpiresAtUtc).IsEqualTo(now.AddMinutes(5));
        await Assert.That(validatingService.Matches(issued.Token, actor, snapshot)).IsFalse();
    }

    private static EmailDeliveryDisableImpactSnapshot CreateSnapshot() =>
        new(
            TenantId: null,
            Revision: 3,
            IsLocked: false,
            AffectedScopes: [new(TenantId: null, Revision: 3), new(TenantId: Guid.NewGuid(), Revision: 8)]);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
