using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Domain.AddOns.MutationTests;

public sealed class EventAddOnMutationTests
{
    private static readonly DateTime UtcNow =
        new(2025, 8, 29, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CatalogLifecycleValidatesLineageAndFreezesPublishedOffers()
    {
        Guid tenantId = NewId();
        Guid eventId = NewId();
        EventAddOnCatalogVersion catalog =
            EventAddOnCatalogVersion.Create(tenantId, eventId, "eur", 1);
        EventAddOnCatalogItem item = Item(catalog, "Lunch", 1_200, 20);

        await Assert.That(catalog.CurrencyCode).IsEqualTo("EUR");
        await Assert.That(catalog.VersionNumber).IsEqualTo(1);
        await Assert.That(catalog.IsPublished).IsFalse();
        await Assert.That(() => catalog.Publish(UtcNow))
            .Throws<InvalidOperationException>();

        Guid draftStamp = catalog.ConcurrencyStamp;
        catalog.AddItem(item);
        await Assert.That(catalog.Items.Count).IsEqualTo(1);
        await Assert.That(catalog.ConcurrencyStamp).IsNotEqualTo(draftStamp);
        await Assert.That(() => catalog.AddItem(item))
            .Throws<ArgumentException>();

        catalog.Publish(UtcNow);
        await Assert.That(catalog.PublishedAt).IsEqualTo(UtcNow);
        await Assert.That(catalog.IsPublished).IsTrue();
        await Assert.That(() => catalog.AddItem(Item(catalog, "Parking", 500, 5)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => catalog.Publish(UtcNow.AddMinutes(1)))
            .Throws<InvalidOperationException>();

        catalog.Retire(UtcNow.AddMinutes(2));
        await Assert.That(catalog.RetiredAt).IsEqualTo(UtcNow.AddMinutes(2));
        await Assert.That(catalog.IsPublished).IsFalse();
        await Assert.That(() => catalog.Retire(UtcNow.AddMinutes(3)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CatalogFactoryAndTransitionsRejectEveryInvalidBoundary()
    {
        Guid tenantId = NewId();
        Guid eventId = NewId();
        await Assert.That(() =>
                EventAddOnCatalogVersion.Create(Guid.Empty, eventId, "EUR", 1))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                EventAddOnCatalogVersion.Create(tenantId, Guid.Empty, "EUR", 1))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                EventAddOnCatalogVersion.Create(tenantId, eventId, "EUR", 0))
            .Throws<ArgumentOutOfRangeException>();

        EventAddOnCatalogVersion catalog =
            EventAddOnCatalogVersion.Create(tenantId, eventId, "EUR", 1);
        await Assert.That(() => catalog.AddItem(null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => catalog.Retire(UtcNow))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
                catalog.AddItem(EventAddOnCatalogItem.Create(
                    NewId(),
                    NewId(),
                    catalog.Id,
                    "Foreign tenant",
                    null,
                    Money.Create(1, "EUR"),
                    1,
                    "Fulfill",
                    "Refund")))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                catalog.AddItem(EventAddOnCatalogItem.Create(
                    NewId(),
                    tenantId,
                    NewId(),
                    "Foreign catalog",
                    null,
                    Money.Create(1, "EUR"),
                    1,
                    "Fulfill",
                    "Refund")))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                catalog.AddItem(EventAddOnCatalogItem.Create(
                    NewId(),
                    tenantId,
                    catalog.Id,
                    "Wrong currency",
                    null,
                    Money.Create(1, "USD"),
                    1,
                    "Fulfill",
                    "Refund")))
            .Throws<ArgumentException>();

        catalog.AddItem(Item(catalog, "Valid", 1, 1));
        await Assert.That(() =>
                catalog.Publish(DateTime.SpecifyKind(UtcNow, DateTimeKind.Local)))
            .Throws<ArgumentException>();
        await Assert.That(() => catalog.Publish(DateTime.UtcNow.AddDays(1)))
            .Throws<ArgumentException>();
        catalog.Publish(UtcNow);
        await Assert.That(() => catalog.Retire(UtcNow.AddMinutes(-1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
                catalog.Retire(DateTime.SpecifyKind(UtcNow, DateTimeKind.Local)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ItemFactoryNormalizesDisclosureAndRejectsInvalidCapacity()
    {
        EventAddOnCatalogVersion catalog =
            EventAddOnCatalogVersion.Create(NewId(), NewId(), "EUR", 2);
        EventAddOnCatalogItem item = EventAddOnCatalogItem.Create(
            NewId(),
            catalog.TenantId,
            catalog.Id,
            "  Meal package  ",
            "  Vegetarian meal  ",
            Money.Create(750, "EUR"),
            12,
            "  Collect at desk.  ",
            "  Refund before fulfillment.  ");

        await Assert.That(item.Name).IsEqualTo("Meal package");
        await Assert.That(item.Description).IsEqualTo("Vegetarian meal");
        await Assert.That(item.UnitPriceMinor).IsEqualTo(750L);
        await Assert.That(item.CurrencyCode).IsEqualTo("EUR");
        await Assert.That(item.InventoryCapacity).IsEqualTo(12);
        await Assert.That(item.FulfillmentDisclosure).IsEqualTo("Collect at desk.");
        await Assert.That(item.RefundDisclosure).IsEqualTo("Refund before fulfillment.");
        await Assert.That(() => EventAddOnCatalogItem.Create(
                NewId(),
                catalog.TenantId,
                catalog.Id,
                "Item",
                null,
                Money.Create(1, "EUR"),
                0,
                "Fulfill",
                "Refund"))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ItemFactoryRejectsMissingIdentityMoneyAndOversizedCopy()
    {
        Guid tenantId = NewId();
        Guid catalogId = NewId();
        await Assert.That(() => EventAddOnCatalogItem.Create(
                Guid.Empty,
                tenantId,
                catalogId,
                "Item",
                null,
                Money.Create(1, "EUR"),
                1,
                "Fulfill",
                "Refund"))
            .Throws<ArgumentException>();
        await Assert.That(() => EventAddOnCatalogItem.Create(
                NewId(),
                Guid.Empty,
                catalogId,
                "Item",
                null,
                Money.Create(1, "EUR"),
                1,
                "Fulfill",
                "Refund"))
            .Throws<ArgumentException>();
        await Assert.That(() => EventAddOnCatalogItem.Create(
                NewId(),
                tenantId,
                Guid.Empty,
                "Item",
                null,
                Money.Create(1, "EUR"),
                1,
                "Fulfill",
                "Refund"))
            .Throws<ArgumentException>();
        await Assert.That(() => EventAddOnCatalogItem.Create(
                NewId(),
                tenantId,
                catalogId,
                "Item",
                null,
                null!,
                1,
                "Fulfill",
                "Refund"))
            .Throws<ArgumentNullException>();
        await Assert.That(() => EventAddOnCatalogItem.Create(
                NewId(),
                tenantId,
                catalogId,
                " ",
                null,
                Money.Create(1, "EUR"),
                1,
                "Fulfill",
                "Refund"))
            .Throws<ArgumentException>();
        await Assert.That(() => EventAddOnCatalogItem.Create(
                NewId(),
                tenantId,
                catalogId,
                new string('n', EventAddOnCatalogItem.MaxNameLength + 1),
                null,
                Money.Create(1, "EUR"),
                1,
                "Fulfill",
                "Refund"))
            .Throws<ArgumentException>();
        await Assert.That(() => EventAddOnCatalogItem.Create(
                NewId(),
                tenantId,
                catalogId,
                "Item",
                new string('d', EventAddOnCatalogItem.MaxDescriptionLength + 1),
                Money.Create(1, "EUR"),
                1,
                "Fulfill",
                "Refund"))
            .Throws<ArgumentException>();
        await Assert.That(() => EventAddOnCatalogItem.Create(
                NewId(),
                tenantId,
                catalogId,
                "Item",
                null,
                Money.Create(1, "EUR"),
                1,
                " ",
                "Refund"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task OrderLineSnapshotsLiteralCheckedTotalAndPinnedCatalog()
    {
        Guid tenantId = NewId();
        Guid eventId = NewId();
        RegistrationOrder order = Order(tenantId, eventId);
        EventAddOnCatalogVersion catalog =
            EventAddOnCatalogVersion.Create(tenantId, eventId, "EUR", 1);
        EventAddOnCatalogItem item = Item(catalog, "Lunch", 1_200, 20);
        catalog.AddItem(item);
        catalog.Publish(UtcNow);

        order.PinAddOnCatalog(catalog);
        RegistrationOrderAddOnLine line =
            RegistrationOrderAddOnLine.Create(NewId(), order, catalog, item, 2);
        order.AddAddOnLine(line);

        await Assert.That(order.AddOnCatalogVersionIdSnapshot).IsEqualTo(catalog.Id);
        await Assert.That(order.AddOnLines.Count).IsEqualTo(1);
        await Assert.That(order.AddOnTotalMinorSnapshot).IsEqualTo(2_400L);
        await Assert.That(line.Quantity).IsEqualTo(2);
        await Assert.That(line.NameSnapshot).IsEqualTo("Lunch");
        await Assert.That(line.UnitPriceMinorSnapshot).IsEqualTo(1_200L);
        await Assert.That(line.LineTotalMinorSnapshot).IsEqualTo(2_400L);
        await Assert.That(line.CurrencyCodeSnapshot).IsEqualTo("EUR");
        await Assert.That(() => order.AddAddOnLine(line))
            .Throws<ArgumentException>();

        RegistrationOrder overflowOrder = Order(tenantId, eventId);
        EventAddOnCatalogVersion overflowCatalog =
            EventAddOnCatalogVersion.Create(tenantId, eventId, "EUR", 2);
        EventAddOnCatalogItem overflow =
            Item(overflowCatalog, "Overflow", long.MaxValue, 2);
        overflowCatalog.AddItem(overflow);
        overflowCatalog.Publish(UtcNow);
        overflowOrder.PinAddOnCatalog(overflowCatalog);
        await Assert.That(() =>
                RegistrationOrderAddOnLine.Create(
                    NewId(),
                    overflowOrder,
                    overflowCatalog,
                    overflow,
                    2))
            .Throws<OverflowException>();
    }

    [Test]
    public async Task OrderLineFactoryRejectsMissingAndMismatchedCommercialFacts()
    {
        Guid tenantId = NewId();
        Guid eventId = NewId();
        RegistrationOrder order = Order(tenantId, eventId);
        EventAddOnCatalogVersion catalog =
            EventAddOnCatalogVersion.Create(tenantId, eventId, "EUR", 1);
        EventAddOnCatalogItem item = Item(catalog, "Valid", 100, 2);

        await Assert.That(() =>
                RegistrationOrderAddOnLine.Create(
                    NewId(),
                    order,
                    catalog,
                    item,
                    1))
            .Throws<InvalidOperationException>();

        catalog.AddItem(item);
        catalog.Publish(UtcNow);
        order.PinAddOnCatalog(catalog);
        await Assert.That(() =>
                RegistrationOrderAddOnLine.Create(
                    Guid.Empty,
                    order,
                    catalog,
                    item,
                    1))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                RegistrationOrderAddOnLine.Create(
                    NewId(),
                    order,
                    catalog,
                    item,
                    0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() =>
                RegistrationOrderAddOnLine.Create(
                    NewId(),
                    null!,
                    catalog,
                    item,
                    1))
            .Throws<ArgumentNullException>();
        await Assert.That(() =>
                RegistrationOrderAddOnLine.Create(
                    NewId(),
                    order,
                    null!,
                    item,
                    1))
            .Throws<ArgumentNullException>();
        await Assert.That(() =>
                RegistrationOrderAddOnLine.Create(
                    NewId(),
                    order,
                    catalog,
                    null!,
                    1))
            .Throws<ArgumentNullException>();

        EventAddOnCatalogItem foreignItem = EventAddOnCatalogItem.Create(
            NewId(),
            tenantId,
            catalog.Id,
            "Not attached",
            null,
            Money.Create(100, "EUR"),
            1,
            "Fulfill",
            "Refund");
        await Assert.That(() =>
                RegistrationOrderAddOnLine.Create(
                    NewId(),
                    order,
                    catalog,
                    foreignItem,
                    1))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InventoryAllocationReleasesMonotonicallyAndClosesActiveSlot()
    {
        RegistrationOrderAddOnLine line = Line(quantity: 3, unitPriceMinor: 333);
        EventAddOnInventoryAllocation allocation =
            EventAddOnInventoryAllocation.Create(NewId(), NewId(), line, UtcNow);

        await Assert.That(allocation.Quantity).IsEqualTo(3);
        await Assert.That(allocation.ReleasedQuantity).IsEqualTo(0);
        await Assert.That(allocation.AllocatedQuantity).IsEqualTo(3);
        await Assert.That(allocation.ActiveUniquenessSlot).IsEqualTo(line.Id);

        allocation.ReleaseQuantity(1, UtcNow.AddMinutes(1));
        await Assert.That(allocation.ReleasedQuantity).IsEqualTo(1);
        await Assert.That(allocation.AllocatedQuantity).IsEqualTo(2);
        await Assert.That(allocation.ActiveUniquenessSlot).IsEqualTo(line.Id);
        await Assert.That(() => allocation.ReleaseQuantity(3, UtcNow.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => allocation.ReleaseQuantity(1, UtcNow))
            .Throws<ArgumentException>();

        allocation.ReleaseQuantity(2, UtcNow.AddMinutes(2));
        await Assert.That(allocation.AllocatedQuantity).IsEqualTo(0);
        await Assert.That(allocation.ActiveUniquenessSlot).IsNull();
    }

    [Test]
    public async Task InventoryFactoryAndReleaseRejectMalformedAuthority()
    {
        RegistrationOrderAddOnLine line = Line(quantity: 2, unitPriceMinor: 500);
        await Assert.That(() =>
                EventAddOnInventoryAllocation.Create(Guid.Empty, NewId(), line, UtcNow))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                EventAddOnInventoryAllocation.Create(NewId(), Guid.Empty, line, UtcNow))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                EventAddOnInventoryAllocation.Create(NewId(), NewId(), null!, UtcNow))
            .Throws<ArgumentNullException>();
        await Assert.That(() =>
                EventAddOnInventoryAllocation.Create(
                    NewId(),
                    NewId(),
                    line,
                    DateTime.SpecifyKind(UtcNow, DateTimeKind.Local)))
            .Throws<ArgumentException>();

        EventAddOnInventoryAllocation allocation =
            EventAddOnInventoryAllocation.Create(NewId(), NewId(), line, UtcNow);
        await Assert.That(() => allocation.ReleaseQuantity(0, UtcNow.AddMinutes(1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() =>
                allocation.ReleaseQuantity(
                    1,
                    DateTime.SpecifyKind(UtcNow, DateTimeKind.Local)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task FulfillmentAndRefundStatesAreReplaySafeAndMonotonic()
    {
        RegistrationOrderAddOnLine line = Line(quantity: 3, unitPriceMinor: 333);
        Guid fulfillmentOperationId = NewId();
        EventAddOnFulfillment fulfillment =
            EventAddOnFulfillment.Create(
                NewId(),
                fulfillmentOperationId,
                line,
                UtcNow);
        await Assert.That(fulfillment.OperationId).IsEqualTo(fulfillmentOperationId);
        await Assert.That(fulfillment.RegistrationOrderAddOnLineId).IsEqualTo(line.Id);
        await Assert.That(fulfillment.FulfilledAt).IsEqualTo(UtcNow);

        EventAddOnRefundAllocation confirmed =
            EventAddOnRefundAllocation.Create(NewId(), NewId(), line, 2, UtcNow);
        await Assert.That(confirmed.Quantity).IsEqualTo(2);
        await Assert.That(confirmed.AmountMinor).IsEqualTo(666L);
        await Assert.That(confirmed.Status)
            .IsEqualTo(EventAddOnRefundAllocationStatus.PendingProvider);
        await Assert.That(confirmed.TryConfirm(UtcNow.AddMinutes(1))).IsTrue();
        await Assert.That(confirmed.TryConfirm(UtcNow.AddMinutes(2))).IsFalse();
        await Assert.That(confirmed.ConfirmedAt).IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(() => confirmed.TryFail(UtcNow.AddMinutes(2)))
            .Throws<InvalidOperationException>();

        EventAddOnRefundAllocation failed =
            EventAddOnRefundAllocation.Create(NewId(), NewId(), line, 1, UtcNow);
        await Assert.That(failed.TryFail(UtcNow.AddMinutes(1))).IsTrue();
        await Assert.That(failed.TryFail(UtcNow.AddMinutes(2))).IsFalse();
        await Assert.That(failed.FailedAt).IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(() => failed.TryConfirm(UtcNow.AddMinutes(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FulfillmentAndRefundFactoriesRejectMalformedAuthority()
    {
        RegistrationOrderAddOnLine line = Line(quantity: 2, unitPriceMinor: 500);
        await Assert.That(() =>
                EventAddOnFulfillment.Create(Guid.Empty, NewId(), line, UtcNow))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                EventAddOnFulfillment.Create(NewId(), Guid.Empty, line, UtcNow))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                EventAddOnFulfillment.Create(NewId(), NewId(), null!, UtcNow))
            .Throws<ArgumentNullException>();
        await Assert.That(() =>
                EventAddOnFulfillment.Create(
                    NewId(),
                    NewId(),
                    line,
                    DateTime.SpecifyKind(UtcNow, DateTimeKind.Local)))
            .Throws<ArgumentException>();

        await Assert.That(() =>
                EventAddOnRefundAllocation.Create(Guid.Empty, NewId(), line, 1, UtcNow))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                EventAddOnRefundAllocation.Create(NewId(), Guid.Empty, line, 1, UtcNow))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                EventAddOnRefundAllocation.Create(NewId(), NewId(), null!, 1, UtcNow))
            .Throws<ArgumentNullException>();
        await Assert.That(() =>
                EventAddOnRefundAllocation.Create(NewId(), NewId(), line, 0, UtcNow))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() =>
                EventAddOnRefundAllocation.Create(NewId(), NewId(), line, 3, UtcNow))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() =>
                EventAddOnRefundAllocation.Create(
                    NewId(),
                    NewId(),
                    line,
                    1,
                    DateTime.SpecifyKind(UtcNow, DateTimeKind.Local)))
            .Throws<ArgumentException>();

        EventAddOnRefundAllocation allocation =
            EventAddOnRefundAllocation.Create(NewId(), NewId(), line, 1, UtcNow);
        await Assert.That(() => allocation.TryConfirm(UtcNow.AddMinutes(-1)))
            .Throws<ArgumentException>();
        await Assert.That(() => allocation.TryFail(UtcNow.AddMinutes(-1)))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                allocation.TryConfirmInventoryReleasePending(UtcNow.AddMinutes(-1)))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                allocation.TryCompleteInventoryRelease(UtcNow.AddMinutes(1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ConfirmedRefundCanRecoverPendingInventoryReleaseExactlyOnce()
    {
        RegistrationOrderAddOnLine line = Line(quantity: 2, unitPriceMinor: 500);
        EventAddOnRefundAllocation allocation =
            EventAddOnRefundAllocation.Create(NewId(), NewId(), line, 1, UtcNow);

        await Assert.That(
                allocation.TryConfirmInventoryReleasePending(UtcNow.AddMinutes(1)))
            .IsTrue();
        await Assert.That(allocation.Status)
            .IsEqualTo(EventAddOnRefundAllocationStatus.ConfirmedInventoryReleasePending);
        await Assert.That(
                allocation.TryConfirmInventoryReleasePending(UtcNow.AddMinutes(2)))
            .IsFalse();
        await Assert.That(
                allocation.TryCompleteInventoryRelease(UtcNow.AddMinutes(2)))
            .IsTrue();
        await Assert.That(allocation.Status)
            .IsEqualTo(EventAddOnRefundAllocationStatus.Confirmed);
        await Assert.That(
                allocation.TryCompleteInventoryRelease(UtcNow.AddMinutes(3)))
            .IsFalse();
    }

    [Test]
    public async Task ResultFactoriesExposeOnlyValidOutcomeAndPayloadPairs()
    {
        RegistrationOrderAddOnLine line = Line(quantity: 1, unitPriceMinor: 500);
        EventAddOnInventoryAllocation inventory =
            EventAddOnInventoryAllocation.Create(NewId(), NewId(), line, UtcNow);
        EventAddOnFulfillment fulfillment =
            EventAddOnFulfillment.Create(NewId(), NewId(), line, UtcNow);
        EventAddOnRefundAllocation refund =
            EventAddOnRefundAllocation.Create(NewId(), NewId(), line, 1, UtcNow);

        await Assert.That(EventAddOnInventoryResult.Reserved(inventory).Outcome)
            .IsEqualTo(EventAddOnInventoryOutcome.Reserved);
        await Assert.That(EventAddOnInventoryResult.Existing(inventory).Allocation)
            .IsEqualTo(inventory);
        await Assert.That(
                EventAddOnInventoryResult.Failure(
                    EventAddOnInventoryOutcome.InsufficientInventory).Allocation)
            .IsNull();
        await Assert.That(
                EventAddOnInventoryResult.Failure(
                    EventAddOnInventoryOutcome.NotFound).Outcome)
            .IsEqualTo(EventAddOnInventoryOutcome.NotFound);
        await Assert.That(
                EventAddOnInventoryResult.Failure(
                    EventAddOnInventoryOutcome.TenantMismatch).Outcome)
            .IsEqualTo(EventAddOnInventoryOutcome.TenantMismatch);
        await Assert.That(() =>
                EventAddOnInventoryResult.Failure(EventAddOnInventoryOutcome.Reserved))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(EventAddOnFulfillmentResult.Fulfilled(fulfillment).Outcome)
            .IsEqualTo(EventAddOnFulfillmentOutcome.Fulfilled);
        await Assert.That(EventAddOnFulfillmentResult.Existing(fulfillment).Fulfillment)
            .IsEqualTo(fulfillment);
        await Assert.That(
                EventAddOnFulfillmentResult.Failure(
                    EventAddOnFulfillmentOutcome.NotReserved).Outcome)
            .IsEqualTo(EventAddOnFulfillmentOutcome.NotReserved);
        await Assert.That(
                EventAddOnFulfillmentResult.Failure(
                    EventAddOnFulfillmentOutcome.NotFound).Outcome)
            .IsEqualTo(EventAddOnFulfillmentOutcome.NotFound);
        await Assert.That(
                EventAddOnFulfillmentResult.Failure(
                    EventAddOnFulfillmentOutcome.TenantMismatch).Outcome)
            .IsEqualTo(EventAddOnFulfillmentOutcome.TenantMismatch);
        await Assert.That(() =>
                EventAddOnFulfillmentResult.Failure(EventAddOnFulfillmentOutcome.Fulfilled))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(EventAddOnRefundResult.Allocated(refund).Outcome)
            .IsEqualTo(EventAddOnRefundOutcome.Allocated);
        await Assert.That(EventAddOnRefundResult.Existing(refund).Allocation)
            .IsEqualTo(refund);
        await Assert.That(
                EventAddOnRefundResult.Failure(
                    EventAddOnRefundOutcome.ProviderFailed).Allocation)
            .IsNull();
        await Assert.That(
                EventAddOnRefundResult.Failure(
                    EventAddOnRefundOutcome.ExceedsCapturedAmount).Outcome)
            .IsEqualTo(EventAddOnRefundOutcome.ExceedsCapturedAmount);
        await Assert.That(
                EventAddOnRefundResult.Failure(
                    EventAddOnRefundOutcome.NotFound).Outcome)
            .IsEqualTo(EventAddOnRefundOutcome.NotFound);
        await Assert.That(
                EventAddOnRefundResult.Failure(
                    EventAddOnRefundOutcome.TenantMismatch).Outcome)
            .IsEqualTo(EventAddOnRefundOutcome.TenantMismatch);
        await Assert.That(() =>
                EventAddOnRefundResult.Failure(EventAddOnRefundOutcome.Allocated))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static RegistrationOrderAddOnLine Line(
        int quantity,
        long unitPriceMinor)
    {
        Guid tenantId = NewId();
        Guid eventId = NewId();
        RegistrationOrder order = Order(tenantId, eventId);
        EventAddOnCatalogVersion catalog =
            EventAddOnCatalogVersion.Create(tenantId, eventId, "EUR", 1);
        EventAddOnCatalogItem item =
            Item(catalog, "Add-on", unitPriceMinor, quantity);
        catalog.AddItem(item);
        catalog.Publish(UtcNow);
        order.PinAddOnCatalog(catalog);
        return RegistrationOrderAddOnLine.Create(
            NewId(),
            order,
            catalog,
            item,
            quantity);
    }

    private static EventAddOnCatalogItem Item(
        EventAddOnCatalogVersion catalog,
        string name,
        long priceMinor,
        int capacity) =>
        EventAddOnCatalogItem.Create(
            NewId(),
            catalog.TenantId,
            catalog.Id,
            name,
            null,
            Money.Create(priceMinor, catalog.CurrencyCode),
            capacity,
            "Collect at the event service desk.",
            "Unfulfilled quantities may be refunded.");

    private static RegistrationOrder Order(Guid tenantId, Guid eventId) =>
        RegistrationOrder.Create(
            tenantId,
            eventId,
            NewId(),
            NewId(),
            BookingPartyTypeEnum.Individual,
            NewId(),
            RegistrationParticipationSnapshot.Create(NewId(), 1, 1, 1, null),
            null,
            null,
            "EUR",
            UtcNow,
            UtcNow.AddHours(1));

    private static Guid NewId() => Guid.CreateVersion7();
}
