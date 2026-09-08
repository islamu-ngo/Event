using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;
using System.Reflection;

namespace Event.Domain.UnitTests.Entities;

public sealed class PromotionReservationTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ApplyPromotion_RepricesSnapshotsFeeAndContributionThenRemoveRestoresAcceptedFacts()
    {
        PlatformFeePolicy feePolicy = PlatformFeePolicy.CreateDefault().CreateRevision(true, 500, [PlatformFeeFixedCharge.Create("USD", 25)]);
        OrderFixture fixture = CreatePricedOrder(linePrices: [800, 200], contributionBasisPoints: 1_000, pinnedFeePolicy: feePolicy);
        PromotionDefinition definition = CreateDefinition(fixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 500, null));
        PromotionCode code = PromotionCode.Create(definition, "SAVE500", definition.ScopeMetadata);
        PromotionReservation reservation = PromotionReservation.Reserve(fixture.Order, definition, code, Now);

        bool applied = fixture.Order.ApplyPromotion(reservation, definition, code, Now, 0, 0, feePolicy);
        bool retry = fixture.Order.ApplyPromotion(reservation, definition, code, Now, 0, 0, feePolicy);

        await Assert.That(applied).IsTrue();
        await Assert.That(retry).IsFalse();
        await Assert.That(fixture.Order.PreDiscountOrganizerDirectedTotalMinorSnapshot).IsEqualTo(1_000);
        await Assert.That(fixture.Order.PromotionDiscountTotalMinorSnapshot).IsEqualTo(500);
        await Assert.That(fixture.Order.PostDiscountOrganizerDirectedTotalMinorSnapshot).IsEqualTo(500);
        await Assert.That(fixture.Order.OrganizerDirectedTotalMinorSnapshot).IsEqualTo(500);
        await Assert.That(fixture.Order.PlatformFeeTotalMinorSnapshot).IsEqualTo(50);
        await Assert.That(fixture.Order.OrganizerEarningsTotalMinorSnapshot).IsEqualTo(450);
        await Assert.That(fixture.Order.PlatformContributionTotalMinorSnapshot).IsEqualTo(50);
        await Assert.That(fixture.Order.TotalDueMinorSnapshot).IsEqualTo(550);
        await Assert.That(fixture.Order.Lines.Sum(line => line.PromotionDiscountAmountMinorSnapshot)).IsEqualTo(500);
        await Assert.That(fixture.Order.Lines.All(line => line.PostDiscountLineSubtotalMinorSnapshot >= 0)).IsTrue();

        bool removed = fixture.Order.RemovePromotion(reservation, Now.AddMinutes(1), feePolicy);
        bool removeRetry = fixture.Order.RemovePromotion(reservation, Now.AddMinutes(2), feePolicy);

        await Assert.That(removed).IsTrue();
        await Assert.That(removeRetry).IsFalse();
        await Assert.That(fixture.Order.PromotionDiscountTotalMinorSnapshot).IsEqualTo(0);
        await Assert.That(fixture.Order.OrganizerDirectedTotalMinorSnapshot).IsEqualTo(1_000);
        await Assert.That(fixture.Order.PlatformFeeTotalMinorSnapshot).IsEqualTo(75);
        await Assert.That(fixture.Order.OrganizerEarningsTotalMinorSnapshot).IsEqualTo(925);
        await Assert.That(fixture.Order.PlatformContributionTotalMinorSnapshot).IsEqualTo(100);
        await Assert.That(fixture.Order.TotalDueMinorSnapshot).IsEqualTo(1_100);
        await Assert.That(fixture.Order.AppliedPromotionCodeIdSnapshot).IsNull();
    }

    [Test]
    public async Task ApplyPromotion_WhenDifferentCodeAlreadyActive_RequiresRemoveFirst()
    {
        OrderFixture fixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null);
        PromotionDefinition definition = CreateDefinition(fixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 100, null));
        PromotionCode firstCode = PromotionCode.Create(definition, "FIRST", definition.ScopeMetadata);
        PromotionCode secondCode = PromotionCode.Create(definition, "SECOND", definition.ScopeMetadata);
        PromotionReservation firstReservation = PromotionReservation.Reserve(fixture.Order, definition, firstCode, Now);
        PromotionReservation secondReservation = PromotionReservation.Reserve(fixture.Order, definition, secondCode, Now);

        fixture.Order.ApplyPromotion(firstReservation, definition, firstCode, Now, 0, 0, null);

        await Assert.That(() => fixture.Order.ApplyPromotion(secondReservation, definition, secondCode, Now, 0, 0, null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NeedsReconciliationRejectsPromotionApplyAndRemove()
    {
        OrderFixture applyFixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null);
        PromotionDefinition applyDefinition = CreateDefinition(applyFixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 100, null));
        PromotionCode applyCode = PromotionCode.Create(applyDefinition, "LATE", applyDefinition.ScopeMetadata);
        PromotionReservation applyReservation = PromotionReservation.Reserve(applyFixture.Order, applyDefinition, applyCode, Now);
        MoveToNeedsReconciliation(applyFixture.Order);

        await Assert.That(() => applyFixture.Order.ApplyPromotion(
                applyReservation, applyDefinition, applyCode, Now, 0, 0, null))
            .Throws<InvalidOperationException>();

        OrderFixture removeFixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null);
        PromotionDefinition removeDefinition = CreateDefinition(removeFixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 100, null));
        PromotionCode removeCode = PromotionCode.Create(removeDefinition, "EARLY", removeDefinition.ScopeMetadata);
        PromotionReservation removeReservation = PromotionReservation.Reserve(removeFixture.Order, removeDefinition, removeCode, Now);
        removeFixture.Order.ApplyPromotion(removeReservation, removeDefinition, removeCode, Now, 0, 0, null);
        MoveToNeedsReconciliation(removeFixture.Order);

        await Assert.That(() => removeFixture.Order.RemovePromotion(removeReservation, Now.AddMinutes(1), null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Reserve_WhenPromotionTargetsAnotherEventOrCatalog_RejectsSameTenantScopeMismatch()
    {
        OrderFixture fixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null);
        PromotionDefinition otherEventDefinition = CreateDefinitionForScope(
            PromotionScopeMetadata.Create(fixture.Catalog.TenantId, Guid.CreateVersion7(), fixture.Catalog.Id, fixture.Catalog.VersionNumber, fixture.Catalog.CurrencyCode));
        PromotionDefinition otherCatalogDefinition = CreateDefinitionForScope(
            PromotionScopeMetadata.Create(fixture.Catalog.TenantId, fixture.Catalog.EventId, Guid.CreateVersion7(), fixture.Catalog.VersionNumber + 1, fixture.Catalog.CurrencyCode));
        PromotionCode otherEventCode = PromotionCode.Create(otherEventDefinition, "EVT", otherEventDefinition.ScopeMetadata);
        PromotionCode otherCatalogCode = PromotionCode.Create(otherCatalogDefinition, "CAT", otherCatalogDefinition.ScopeMetadata);

        await Assert.That(() => PromotionReservation.Reserve(fixture.Order, otherEventDefinition, otherEventCode, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => PromotionReservation.Reserve(fixture.Order, otherCatalogDefinition, otherCatalogCode, Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ApplyPromotion_WhenPromotionTargetsAnotherEventOrCatalog_RejectsBeforeMutatingSnapshots()
    {
        OrderFixture fixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null);
        PromotionDefinition otherEventDefinition = CreateDefinitionForScope(
            PromotionScopeMetadata.Create(fixture.Catalog.TenantId, Guid.CreateVersion7(), fixture.Catalog.Id, fixture.Catalog.VersionNumber, fixture.Catalog.CurrencyCode));
        PromotionDefinition otherCatalogDefinition = CreateDefinitionForScope(
            PromotionScopeMetadata.Create(fixture.Catalog.TenantId, fixture.Catalog.EventId, Guid.CreateVersion7(), fixture.Catalog.VersionNumber + 1, fixture.Catalog.CurrencyCode));
        PromotionCode otherEventCode = PromotionCode.Create(otherEventDefinition, "EVT", otherEventDefinition.ScopeMetadata);
        PromotionCode otherCatalogCode = PromotionCode.Create(otherCatalogDefinition, "CAT", otherCatalogDefinition.ScopeMetadata);
        PromotionReservation otherEventReservation = CreateReservationBypassingScopeGuard(fixture.Order, otherEventDefinition, otherEventCode);
        PromotionReservation otherCatalogReservation = CreateReservationBypassingScopeGuard(fixture.Order, otherCatalogDefinition, otherCatalogCode);

        await Assert.That(() => fixture.Order.ApplyPromotion(otherEventReservation, otherEventDefinition, otherEventCode, Now, 0, 0, null))
            .Throws<ArgumentException>();
        await Assert.That(() => fixture.Order.ApplyPromotion(otherCatalogReservation, otherCatalogDefinition, otherCatalogCode, Now, 0, 0, null))
            .Throws<ArgumentException>();
        await Assert.That(fixture.Order.PromotionDiscountTotalMinorSnapshot).IsEqualTo(0);
        await Assert.That(fixture.Order.AppliedPromotionCodeIdSnapshot).IsNull();
        await Assert.That(fixture.Order.Lines.Single().PromotionDiscountAmountMinorSnapshot).IsEqualTo(0);
    }

    [Test]
    public async Task ApplyPromotion_WhenFeePolicyDoesNotMatchPinnedLineVersion_RejectsBeforeMutatingSnapshots()
    {
        PlatformFeePolicy pinnedPolicy = PlatformFeePolicy.CreateDefault().CreateRevision(true, 500, [PlatformFeeFixedCharge.Create("USD", 25)]);
        PlatformFeePolicy otherPolicy = pinnedPolicy.CreateRevision(true, 600, [PlatformFeeFixedCharge.Create("USD", 30)]);
        OrderFixture fixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null, pinnedFeePolicy: pinnedPolicy);
        PromotionDefinition definition = CreateDefinition(fixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 100, null));
        PromotionCode code = PromotionCode.Create(definition, "FEE", definition.ScopeMetadata);
        PromotionReservation reservation = PromotionReservation.Reserve(fixture.Order, definition, code, Now);

        await Assert.That(() => fixture.Order.ApplyPromotion(reservation, definition, code, Now, 0, 0, null))
            .Throws<InvalidOperationException>();
        await Assert.That(() => fixture.Order.ApplyPromotion(reservation, definition, code, Now, 0, 0, otherPolicy))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Order.PromotionDiscountTotalMinorSnapshot).IsEqualTo(0);
        await Assert.That(fixture.Order.OrganizerDirectedTotalMinorSnapshot).IsEqualTo(1_000);
        await Assert.That(fixture.Order.Lines.Single().PromotionDiscountAmountMinorSnapshot).IsEqualTo(0);
    }

    [Test]
    public async Task RemovePromotion_WhenFeePolicyDoesNotMatchPinnedLineVersion_RejectsBeforeMutatingSnapshots()
    {
        PlatformFeePolicy pinnedPolicy = PlatformFeePolicy.CreateDefault().CreateRevision(true, 500, [PlatformFeeFixedCharge.Create("USD", 25)]);
        PlatformFeePolicy otherPolicy = pinnedPolicy.CreateRevision(true, 600, [PlatformFeeFixedCharge.Create("USD", 30)]);
        OrderFixture fixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null, pinnedFeePolicy: pinnedPolicy);
        PromotionDefinition definition = CreateDefinition(fixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 100, null));
        PromotionCode code = PromotionCode.Create(definition, "FEE", definition.ScopeMetadata);
        PromotionReservation reservation = PromotionReservation.Reserve(fixture.Order, definition, code, Now);
        fixture.Order.ApplyPromotion(reservation, definition, code, Now, 0, 0, pinnedPolicy);

        await Assert.That(() => fixture.Order.RemovePromotion(reservation, Now.AddMinutes(1), null))
            .Throws<InvalidOperationException>();
        await Assert.That(() => fixture.Order.RemovePromotion(reservation, Now.AddMinutes(1), otherPolicy))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Order.PromotionDiscountTotalMinorSnapshot).IsEqualTo(100);
        await Assert.That(fixture.Order.AppliedPromotionCodeIdSnapshot).IsEqualTo(code.Id);
        await Assert.That(reservation.PromotionReservationStatusId).IsEqualTo((int)PromotionReservationStatusEnum.Active);
    }

    [Test]
    public async Task ApplyPromotion_WhenNoLinePinnedFeePolicy_SuppliedPolicyIsRejectedBeforeMutatingSnapshots()
    {
        PlatformFeePolicy suppliedPolicy = PlatformFeePolicy.CreateDefault().CreateRevision(true, 500, [PlatformFeeFixedCharge.Create("USD", 25)]);
        OrderFixture fixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null);
        PromotionDefinition definition = CreateDefinition(fixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 100, null));
        PromotionCode code = PromotionCode.Create(definition, "NOPIN", definition.ScopeMetadata);
        PromotionReservation reservation = PromotionReservation.Reserve(fixture.Order, definition, code, Now);

        await Assert.That(() => fixture.Order.ApplyPromotion(reservation, definition, code, Now, 0, 0, suppliedPolicy))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Order.PromotionDiscountTotalMinorSnapshot).IsEqualTo(0);
        await Assert.That(fixture.Order.AppliedPromotionCodeIdSnapshot).IsNull();
    }

    [Test]
    public async Task Reservation_TerminalTransitionsAreExactOnceAndMoveSlotToOwnId()
    {
        OrderFixture fixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null);
        PromotionDefinition definition = CreateDefinition(fixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 100, null));
        PromotionCode code = PromotionCode.Create(definition, "SAVE100", definition.ScopeMetadata);
        PromotionReservation consumed = PromotionReservation.Reserve(fixture.Order, definition, code, Now);
        PromotionReservation released = PromotionReservation.Reserve(fixture.Order, definition, code, Now);

        bool consumedFirst = consumed.TryConsume(Now.AddMinutes(1));
        bool consumedRetry = consumed.TryConsume(Now.AddMinutes(2));
        bool releasedFirst = released.TryRelease(Now.AddMinutes(1));
        bool releasedRetry = released.TryRelease(Now.AddMinutes(2));

        await Assert.That(consumedFirst).IsTrue();
        await Assert.That(consumedRetry).IsFalse();
        await Assert.That(consumed.OrderReservationSlot).IsEqualTo(consumed.Id);
        await Assert.That(releasedFirst).IsTrue();
        await Assert.That(releasedRetry).IsFalse();
        await Assert.That(released.OrderReservationSlot).IsEqualTo(released.Id);
        await Assert.That(() => released.TryConsume(Now.AddMinutes(3))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ApplyPromotion_WhenDiscountMakesOrderZeroDue_RoutesThroughFreeConfirmation()
    {
        OrderFixture fixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: null);
        PromotionDefinition definition = CreateDefinition(fixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 2_000, null));
        PromotionCode code = PromotionCode.Create(definition, "FREE", definition.ScopeMetadata);
        PromotionReservation reservation = PromotionReservation.Reserve(fixture.Order, definition, code, Now);

        fixture.Order.ApplyPromotion(reservation, definition, code, Now, 0, 0, null);
        fixture.Order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, Now);
        fixture.Order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, Now);
        fixture.Order.TransitionTo(RegistrationOrderRules.GetCheckoutDestination(fixture.Order.TotalDueMinorSnapshot), Now);

        await Assert.That(fixture.Order.TotalDueMinorSnapshot).IsEqualTo(0);
        await Assert.That(fixture.Order.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
    }

    [Test]
    public async Task Contribution_WhenPromotionZeroesOrganizerTotal_IsRetainedAndRestoredOnRemoval()
    {
        OrderFixture fixture = CreatePricedOrder(linePrices: [1_000], contributionBasisPoints: 1_000);
        PromotionDefinition definition = CreateDefinition(fixture.Catalog, PromotionDiscountRule.FixedMinor("USD", 1_000, null));
        PromotionCode code = PromotionCode.Create(definition, "FREE", definition.ScopeMetadata);
        PromotionReservation reservation = PromotionReservation.Reserve(fixture.Order, definition, code, Now);

        fixture.Order.ApplyPromotion(reservation, definition, code, Now, 0, 0, null);

        await Assert.That(fixture.Order.PlatformContribution).IsNotNull();
        await Assert.That(fixture.Order.PlatformContribution!.AmountMinor).IsEqualTo(0);
        await Assert.That(fixture.Order.PlatformContribution.ContributionBasisPointsSnapshot).IsEqualTo(1_000);

        fixture.Order.RemovePromotion(reservation, Now.AddMinutes(1), null);

        await Assert.That(fixture.Order.PlatformContribution!.AmountMinor).IsEqualTo(100);
    }

    [Test]
    public async Task VerifiedPurchaserIdentity_UsesAccountVerifiedEmailThenActorAndNeverGuestCapability()
    {
        RegistrationOrder accountOrder = CreateBareOrder(accountUserId: Guid.CreateVersion7(), purchaserActorId: Guid.CreateVersion7(), withGuestCapability: true);
        RegistrationOrder emailOrder = CreateBareOrder(accountUserId: null, purchaserActorId: Guid.CreateVersion7(), withGuestCapability: true);
        RegistrationOrder actorOrder = CreateBareOrder(accountUserId: null, purchaserActorId: Guid.CreateVersion7(), withGuestCapability: true);
        RegistrationOrder guestOnlyOrder = CreateBareOrder(accountUserId: null, purchaserActorId: null, withGuestCapability: true);
        emailOrder.SetPii(RegistrationOrderPii.CreateFromVerifiedContact(
            emailOrder.Id,
            emailOrder.TenantId,
            "Jane",
            "jane@example.test",
            null,
            null,
            "JANE@EXAMPLE.TEST",
            (int)RegistrationRetentionPolicyEnum.StandardOperational,
            Now));

        await Assert.That(accountOrder.GetVerifiedPurchaserIdentity()!.Kind).IsEqualTo("Account");
        await Assert.That(emailOrder.GetVerifiedPurchaserIdentity()).IsEqualTo(VerifiedPurchaserIdentity.Email("JANE@EXAMPLE.TEST"));
        await Assert.That(actorOrder.GetVerifiedPurchaserIdentity()!.Kind).IsEqualTo("Actor");
        await Assert.That(guestOnlyOrder.GetVerifiedPurchaserIdentity()).IsNull();

        emailOrder.Pii!.Update("Jane", "changed@example.test", null, null, (int)RegistrationRetentionPolicyEnum.StandardOperational, Now.AddMinutes(1));

        await Assert.That(emailOrder.Pii.IsEmailVerified).IsFalse();
        await Assert.That(emailOrder.GetVerifiedPurchaserIdentity()!.Kind).IsEqualTo("Actor");
    }

    private static OrderFixture CreatePricedOrder(long[] linePrices, int? contributionBasisPoints, PlatformFeePolicy? pinnedFeePolicy = null)
    {
        EventTicketCatalogVersion catalog = CreatePublishedCatalog(linePrices);
        RegistrationOrder order = CreateBareOrder(catalog.TenantId, catalog.EventId, catalog.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), true);
        foreach (EventTicketType ticketType in catalog.TicketTypes)
        {
            order.AddLine(RegistrationOrderLine.Create(catalog, ticketType, order.Id, 1, null, pinnedFeePolicy));
        }

        if (contributionBasisPoints.HasValue)
        {
            PlatformContributionSetting setting = PlatformContributionSetting.CreateInitial(
                true,
                "Support ISLAMU",
                "Optional contribution",
                [PlatformContributionOption.Create(0, 0, true), PlatformContributionOption.Create(contributionBasisPoints.Value, 1, false)]);
            order.SetPlatformContribution(RegistrationOrderPlatformContribution.CreateOrNull(
                order.Id,
                order.TenantId,
                setting,
                contributionBasisPoints.Value,
                order.Lines.Sum(line => line.LineSubtotalSnapshot),
                "USD"));
        }

        long organizerTotal = order.Lines.Sum(line => line.LineSubtotalSnapshot);
        long contributionTotal = order.PlatformContribution?.AmountMinor ?? 0;
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", organizerTotal, 0, organizerTotal, contributionTotal));
        return new OrderFixture(catalog, order);
    }

    private static RegistrationOrder CreateBareOrder(Guid? accountUserId, Guid? purchaserActorId, bool withGuestCapability) => CreateBareOrder(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        accountUserId,
        purchaserActorId,
        withGuestCapability);

    private static RegistrationOrder CreateBareOrder(
        Guid tenantId,
        Guid eventId,
        Guid catalogId,
        Guid? accountUserId,
        Guid? purchaserActorId,
        bool withGuestCapability) => RegistrationOrder.Create(
        tenantId,
        eventId,
        accountUserId,
        purchaserActorId,
        BookingPartyTypeEnum.Individual,
        catalogId,
        RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
        null,
        withGuestCapability ? CapabilityTokenHash.Create(Convert.ToBase64String(new byte[32])) : null,
        "USD",
        Now,
        Now.AddMinutes(15));

    private static PromotionDefinition CreateDefinition(EventTicketCatalogVersion catalog, PromotionDiscountRule discountRule)
    {
        return CreateDefinitionForScope(
            PromotionScopeMetadata.Create(catalog.TenantId, catalog.EventId, catalog.Id, catalog.VersionNumber, catalog.CurrencyCode),
            discountRule);
    }

    private static void MoveToNeedsReconciliation(RegistrationOrder order)
    {
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, Now);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, Now);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, Now);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingPayment, Now);
        order.TransitionTo(RegistrationOrderStatusEnum.NeedsReconciliation, Now);
    }

    private static PromotionDefinition CreateDefinitionForScope(PromotionScopeMetadata scopeMetadata) =>
        CreateDefinitionForScope(scopeMetadata, PromotionDiscountRule.FixedMinor(scopeMetadata.CurrencyCode, 100, null));

    private static PromotionDefinition CreateDefinitionForScope(PromotionScopeMetadata scopeMetadata, PromotionDiscountRule discountRule)
    {
        PromotionDefinition definition = PromotionDefinition.CreateDraft(
            scopeMetadata,
            "Promotion",
            PromotionEligibility.AllTickets(),
            discountRule,
            Now.AddHours(-1),
            Now.AddDays(1),
            10,
            1);
        definition.Publish(Now);
        return definition;
    }

    private static EventTicketCatalogVersion CreatePublishedCatalog(long[] fixedPrices)
    {
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "USD", 1);
        foreach ((long fixedPrice, int index) in fixedPrices.Select((fixedPrice, index) => (fixedPrice, index)))
        {
            EventTicketType ticketType = EventTicketType.Create(
                Guid.CreateVersion7(),
                catalog.TenantId,
                catalog.Id,
                $"Ticket {index + 1}",
                "USD",
                TicketPricingModeEnum.Fixed,
                Money.Create(fixedPrice, "USD"),
                null,
                null,
                ParticipantDataCollectionModeEnum.None,
                null,
                null,
                null,
                false,
                false,
                null,
                null,
                null,
                null);
            catalog.AddTicketType(ticketType, null);
            catalog.AddEntitlement(ticketType, TicketTypeEntitlement.CreateForEvent(ticketType.Id, catalog.TenantId, catalog.EventId, 1));
        }

        catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
        catalog.Publish();
        return catalog;
    }

    private static PromotionReservation CreateReservationBypassingScopeGuard(RegistrationOrder order, PromotionDefinition definition, PromotionCode code)
    {
        PromotionDefinition validDefinition = CreateDefinitionForScope(
            PromotionScopeMetadata.Create(order.TenantId, order.EventId, order.TicketCatalogVersionId, definition.ScopeMetadata.TicketCatalogVersionNumber, order.CurrencyCode));
        PromotionCode validCode = PromotionCode.Create(validDefinition, "VALID", validDefinition.ScopeMetadata);
        PromotionReservation reservation = PromotionReservation.Reserve(order, validDefinition, validCode, Now);
        SetPrivateProperty(reservation, nameof(PromotionReservation.PromotionDefinitionVersionId), definition.Id);
        SetPrivateProperty(reservation, nameof(PromotionReservation.PromotionCodeId), code.Id);
        return reservation;
    }

    private static void SetPrivateProperty<TValue>(object target, string propertyName, TValue value)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property {propertyName} was not found.");
        property.SetValue(target, value);
    }

    private sealed record OrderFixture(EventTicketCatalogVersion Catalog, RegistrationOrder Order);
}
