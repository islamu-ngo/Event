using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class PromotionDefinitionTests
{
    [Test]
    public async Task PublishReviseAndRevoke_EnforceVersionedLifecycleAndFutureOnlyRevocation()
    {
        DateTime now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        PromotionScopeMetadata scope = CreateScope();
        PromotionDefinition draft = PromotionDefinition.CreateDraft(
            scope,
            "Early bird",
            PromotionEligibility.ForTicketTypes([LineA]),
            PromotionDiscountRule.FixedMinor("USD", 250, maximumDiscountMinor: null),
            startsAtUtc: now.AddHours(-1),
            endsAtUtc: now.AddDays(1),
            totalRedemptionLimit: 10,
            perVerifiedPurchaserLimit: 1);

        draft.Publish(now);
        PromotionDefinition revision = draft.CreateRevision(
            "Early bird revised",
            PromotionEligibility.AllTickets(),
            PromotionDiscountRule.BasisPoints("USD", 1_000, maximumDiscountMinor: 300),
            startsAtUtc: now.AddHours(-1),
            endsAtUtc: now.AddDays(2),
            totalRedemptionLimit: 20,
            perVerifiedPurchaserLimit: 2);

        await Assert.That(draft.PromotionDefinitionStatusId).IsEqualTo((int)PromotionDefinitionStatusEnum.Published);
        await Assert.That(draft.VersionNumber).IsEqualTo(1);
        await Assert.That(revision.VersionNumber).IsEqualTo(2);
        await Assert.That(revision.PromotionDefinitionStatusId).IsEqualTo((int)PromotionDefinitionStatusEnum.Draft);
        await Assert.That(() => draft.Revoke(now, now.AddMinutes(-1))).Throws<InvalidOperationException>();
        draft.Revoke(now, now.AddMinutes(1));
        await Assert.That(draft.PromotionDefinitionStatusId).IsEqualTo((int)PromotionDefinitionStatusEnum.Revoked);
        await Assert.That(() => draft.CreateRevision(
                "too late",
                PromotionEligibility.AllTickets(),
                PromotionDiscountRule.FixedMinor("USD", 1, null),
                now,
                now.AddDays(1),
                null,
                null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Revoke_WhenEffectiveTimePrecedesDecisionTime_RejectsWithoutChangingPublishedSnapshot()
    {
        DateTime publishedAt = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        DateTime decisionAt = publishedAt.AddHours(4);
        DateTime pastEffectiveAt = publishedAt.AddHours(1);
        PromotionDefinition definition = CreatePublishedDefinition(CreateScope(), PromotionDiscountRule.FixedMinor("USD", 100, null));

        await Assert.That(() => definition.Revoke(decisionAt, pastEffectiveAt)).Throws<InvalidOperationException>();
        await Assert.That(decisionAt).IsAfter(pastEffectiveAt);
        await Assert.That(definition.PromotionDefinitionStatusId).IsEqualTo((int)PromotionDefinitionStatusEnum.Published);
        await Assert.That(definition.RevokedAtUtc).IsNull();
    }

    [Test]
    public async Task PromotionCode_KeepsOnlyScopeMetadataAndMaskedDisplayWithoutPlaintextOrDigest()
    {
        PromotionScopeMetadata scope = CreateScope();
        PromotionDefinition definition = CreatePublishedDefinition(scope, PromotionDiscountRule.FixedMinor("USD", 100, null));

        PromotionCode first = PromotionCode.Create(definition, "ABCD", scope);
        PromotionCode second = PromotionCode.Create(definition, "ABCD", scope);

        await Assert.That(first.Id).IsNotEqualTo(second.Id);
        await Assert.That(first.ScopeMetadata).IsEqualTo(second.ScopeMetadata);
        await Assert.That(first.DisplayLabel).IsEqualTo("****ABCD");
        await Assert.That(typeof(PromotionCode).GetProperties().Select(property => property.Name))
            .DoesNotContain("PlaintextCode")
            .And.DoesNotContain("Digest")
            .And.DoesNotContain("LookupKeyVersion");
    }

    [Test]
    public async Task Allocate_UsesFixedOrBasisPointDiscountCapsAndNeverCreatesNegativeLines()
    {
        DateTime now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        PromotionScopeMetadata scope = CreateScope();
        PromotionDefinition fixedDefinition = CreatePublishedDefinition(scope, PromotionDiscountRule.FixedMinor("USD", 1_500, maximumDiscountMinor: 900));
        PromotionDefinition percentDefinition = CreatePublishedDefinition(scope, PromotionDiscountRule.BasisPoints("USD", 2_500, maximumDiscountMinor: null));
        PromotionDiscountLine[] lines =
        [
            new(LineA, TicketA, "USD", 800),
            new(LineB, TicketB, "USD", 200)
        ];

        PromotionDiscountAllocation fixedAllocation = PromotionDiscountAllocator.Allocate(fixedDefinition, lines, now, currentTotalRedemptions: 0, currentPurchaserRedemptions: 0);
        PromotionDiscountAllocation percentAllocation = PromotionDiscountAllocator.Allocate(percentDefinition, lines, now, currentTotalRedemptions: 0, currentPurchaserRedemptions: 0);

        await Assert.That(fixedAllocation.TotalDiscountMinor).IsEqualTo(900);
        await Assert.That(fixedAllocation.LineAllocations.Single(x => x.LineId == LineA).DiscountMinor).IsEqualTo(720);
        await Assert.That(fixedAllocation.LineAllocations.Single(x => x.LineId == LineB).DiscountMinor).IsEqualTo(180);
        await Assert.That(fixedAllocation.LineAllocations.All(x => x.PostDiscountLineSubtotalMinor >= 0)).IsTrue();
        await Assert.That(percentAllocation.TotalDiscountMinor).IsEqualTo(250);
        await Assert.That(percentAllocation.PostDiscountOrganizerTotalMinor).IsEqualTo(750);
    }

    [Test]
    public async Task Allocate_DistributesLargestRemainderByFractionThenLineId()
    {
        DateTime now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        PromotionDefinition definition = CreatePublishedDefinition(CreateScope(), PromotionDiscountRule.FixedMinor("USD", 1, null));
        PromotionDiscountLine[] lines =
        [
            new(LineC, TicketA, "USD", 1),
            new(LineA, TicketB, "USD", 1),
            new(LineB, TicketC, "USD", 1)
        ];

        PromotionDiscountAllocation allocation = PromotionDiscountAllocator.Allocate(definition, lines, now, currentTotalRedemptions: 0, currentPurchaserRedemptions: 0);

        await Assert.That(allocation.LineAllocations.Single(x => x.LineId == LineA).DiscountMinor).IsEqualTo(1);
        await Assert.That(allocation.LineAllocations.Where(x => x.LineId != LineA).Sum(x => x.DiscountMinor)).IsEqualTo(0);
    }

    [Test]
    public async Task Allocate_RejectsCurrencyMismatchIneligibleWindowsAndExceededLimits()
    {
        DateTime now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        PromotionDefinition definition = CreatePublishedDefinition(CreateScope(), PromotionDiscountRule.FixedMinor("USD", 100, null));
        PromotionDiscountLine[] lines = [new(LineA, TicketA, "USD", 500)];

        await Assert.That(() => PromotionDiscountAllocator.Allocate(definition, [new(LineA, TicketA, "EUR", 500)], now, 0, 0))
            .Throws<ArgumentException>();
        await Assert.That(() => PromotionDiscountAllocator.Allocate(definition, lines, now.AddDays(2), 0, 0))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PromotionDiscountAllocator.Allocate(definition, lines, now, 10, 0))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PromotionDiscountAllocator.Allocate(definition, lines, now, 0, 1))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PromotionDiscountRule.FixedMinor("XXX", 100, null)).Throws<ArgumentException>();
    }

    private static readonly Guid LineA = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    private static readonly Guid LineB = Guid.Parse("018f0000-0000-7000-8000-000000000002");
    private static readonly Guid LineC = Guid.Parse("018f0000-0000-7000-8000-000000000003");
    private static readonly Guid TicketA = Guid.Parse("018f0000-0000-7000-8000-000000000101");
    private static readonly Guid TicketB = Guid.Parse("018f0000-0000-7000-8000-000000000102");
    private static readonly Guid TicketC = Guid.Parse("018f0000-0000-7000-8000-000000000103");

    private static PromotionScopeMetadata CreateScope() => PromotionScopeMetadata.Create(
        Guid.Parse("018f0000-0000-7000-8000-000000000201"),
        Guid.Parse("018f0000-0000-7000-8000-000000000202"),
        Guid.Parse("018f0000-0000-7000-8000-000000000203"),
        1,
        "USD");

    private static PromotionDefinition CreatePublishedDefinition(PromotionScopeMetadata scope, PromotionDiscountRule discountRule)
    {
        DateTime now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        PromotionDefinition definition = PromotionDefinition.CreateDraft(
            scope,
            "Promotion",
            PromotionEligibility.AllTickets(),
            discountRule,
            now.AddHours(-1),
            now.AddDays(1),
            totalRedemptionLimit: 10,
            perVerifiedPurchaserLimit: 1);
        definition.Publish(now);
        return definition;
    }
}
