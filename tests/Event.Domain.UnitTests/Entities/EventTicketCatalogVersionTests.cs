using System.Reflection;

using Explore.Domain.Enums;

using Explore.Domain.ValueObjects;
namespace Event.Domain.UnitTests.Entities;

public sealed class EventTicketCatalogVersionTests
{
    [Test]
    public async Task Publish_ThenRetire_FreezesTheCatalogGraph()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = AddFreeTicketWithEventEntitlement(catalog);

        catalog.Publish();

        await Assert.That(catalog.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Published);
        await Assert.That(() => catalog.UpdateTicketPricing(ticketType, TicketPricingModeEnum.Fixed, Money.Create(1_000, "USD"), null, null))
            .Throws<InvalidOperationException>();

        catalog.Retire();

        await Assert.That(catalog.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Retired);
        await Assert.That(() => catalog.AddEntitlement(ticketType, CreateEventEntitlement(catalog, ticketType)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ValidateForPublication_DoesNotMutateCatalogStatus()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        AddFreeTicketWithEventEntitlement(catalog);

        catalog.ValidateForPublication();

        await Assert.That(catalog.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Draft);
    }

    [Test]
    public async Task ValidateForPublication_WhenInvalid_DoesNotMutateCatalogStatus()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();

        await Assert.That(() => catalog.ValidateForPublication()).Throws<InvalidOperationException>();
        await Assert.That(catalog.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Draft);
    }

    [Test]
    public async Task UpdateCommercialDisclosures_NormalizesBoundedTextAndRequiresDraft()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();

        catalog.UpdateCommercialDisclosures("  merchant  ", " refund ", " support ");

        await Assert.That(catalog.MerchantDisclosureText).IsEqualTo("merchant");
        await Assert.That(catalog.RefundPolicyDisclosureText).IsEqualTo("refund");
        await Assert.That(catalog.SupportContactDisclosureText).IsEqualTo("support");

        AddFreeTicketWithEventEntitlement(catalog);
        catalog.Publish();

        await Assert.That(() => catalog.UpdateCommercialDisclosures("merchant", "refund", "support"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UpdateCommercialDisclosures_RejectsOverlongText()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        string overlong = new('x', EventTicketCatalogVersion.MaxCommercialDisclosureTextLength + 1);

        await Assert.That(() => catalog.UpdateCommercialDisclosures(overlong, "refund", "support"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Publish_WhenCatalogContainsPaidTickets_RequiresAllCommercialDisclosures()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = CreateTicket(catalog, "USD", TicketPricingModeEnum.Fixed, 1_000, null, null);
        catalog.AddTicketType(ticketType, null);
        catalog.AddEntitlement(ticketType, CreateEventEntitlement(catalog, ticketType));

        await Assert.That(() => catalog.Publish()).Throws<InvalidOperationException>();

        catalog.UpdateCommercialDisclosures("Merchant of record: organizer", "Refunds within 14 days", "support@example.test");
        catalog.Publish();

        await Assert.That(catalog.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Published);
    }

    [Test]
    public async Task Publish_WhenCatalogIsFree_AllowsMissingCommercialDisclosures()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        AddFreeTicketWithEventEntitlement(catalog);

        catalog.Publish();

        await Assert.That(catalog.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Published);
        await Assert.That(catalog.MerchantDisclosureText).IsNull();
    }

    [Test]
    public async Task Publish_WhenOnlyTicketTypeIsDeleted_RejectsCatalog()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = AddFreeTicketWithEventEntitlement(catalog);
        catalog.DeleteTicketType(ticketType, DomainTestClock.UtcNow, Guid.CreateVersion7());

        await Assert.That(() => catalog.Publish()).Throws<InvalidOperationException>();
        await Assert.That(catalog.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Draft);
    }

    [Test]
    public async Task Publish_IgnoresDeletedTicketTypesAndValidatesLiveGraph()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        AddFreeTicketWithEventEntitlement(catalog);
        EventTicketType deletedTicketType = CreateTicket(catalog, "USD", TicketPricingModeEnum.Free, null, null, null);
        catalog.AddTicketType(deletedTicketType, null);
        catalog.DeleteTicketType(deletedTicketType, DomainTestClock.UtcNow, Guid.CreateVersion7());

        catalog.Publish();

        await Assert.That(catalog.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Published);
    }

    [Test]
    public async Task CloneToDraft_CreatesIndependentTicketAndEntitlementGraph()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = AddFreeTicketWithEventEntitlement(catalog);
        catalog.UpdateCommercialDisclosures("merchant", "refund", "support");
        catalog.Publish();

        EventTicketCatalogVersion clone = catalog.CloneToDraft();
        EventTicketType clonedTicketType = clone.TicketTypes.Single();

        clone.UpdateTicketPricing(clonedTicketType, TicketPricingModeEnum.Fixed, Money.Create(1_235, "USD"), null, null);

        await Assert.That(clone.Id).IsNotEqualTo(catalog.Id);
        await Assert.That(clone.VersionNumber).IsEqualTo(2);
        await Assert.That(clone.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Draft);
        await Assert.That(clonedTicketType.Id).IsNotEqualTo(ticketType.Id);
        await Assert.That(clonedTicketType.Entitlements.Single().Id).IsNotEqualTo(ticketType.Entitlements.Single().Id);
        await Assert.That(clonedTicketType.FixedPriceMinor).IsEqualTo(1_235);
        await Assert.That(clone.MerchantDisclosureText).IsEqualTo("merchant");
        await Assert.That(clone.RefundPolicyDisclosureText).IsEqualTo("refund");
        await Assert.That(clone.SupportContactDisclosureText).IsEqualTo("support");
        await Assert.That(ticketType.TicketPricingModeId).IsEqualTo((int)TicketPricingModeEnum.Free);
    }

    [Test]
    public async Task CloneToDraft_DoesNotResurrectDeletedTicketTypes()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType liveTicketType = AddFreeTicketWithEventEntitlement(catalog);
        EventTicketType deletedTicketType = CreateTicket(catalog, "USD", TicketPricingModeEnum.Free, null, null, null);
        catalog.AddTicketType(deletedTicketType, null);
        catalog.DeleteTicketType(deletedTicketType, DomainTestClock.UtcNow, Guid.CreateVersion7());
        catalog.Publish();

        EventTicketCatalogVersion clone = catalog.CloneToDraft();

        await Assert.That(clone.TicketTypes).HasSingleItem();
        await Assert.That(clone.TicketTypes.Single().Name).IsEqualTo(liveTicketType.Name);
    }

    [Test]
    public async Task UpdateAndDeleteTicketType_RequireDraftAndReplaceAuthoringFields()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = AddFreeTicketWithEventEntitlement(catalog);

        catalog.UpdateTicketType(ticketType, "Student", TicketPricingModeEnum.Fixed, Money.Create(2_500, "USD"), null, null,
            ParticipantDataCollectionModeEnum.PerTicketRequired, null, 12, 18, true, true, 2, 3, 4, 5, ticketType.Entitlements);

        await Assert.That(ticketType.Name).IsEqualTo("Student");
        await Assert.That(ticketType.FixedPriceMinor).IsEqualTo(2_500);
        await Assert.That(ticketType.MinimumAge).IsEqualTo(12);
        await Assert.That(ticketType.RequiresApproval).IsTrue();
        await Assert.That(ticketType.PerBookingPartyLimit).IsEqualTo(5);

        catalog.UpdateCommercialDisclosures("merchant", "refund", "support");
        catalog.Publish();
        await Assert.That(() => catalog.DeleteTicketType(ticketType, DomainTestClock.UtcNow, Guid.CreateVersion7()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EventTicketTypeMutators_AreNonPublic_AndCatalogOwnsMutationSeam()
    {
        MethodInfo? update = typeof(EventTicketType).GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? delete = typeof(EventTicketType).GetMethod(
            "Delete",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? aggregateUpdate = typeof(EventTicketCatalogVersion).GetMethod(
            nameof(EventTicketCatalogVersion.UpdateTicketType),
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo? aggregateDelete = typeof(EventTicketCatalogVersion).GetMethod(
            nameof(EventTicketCatalogVersion.DeleteTicketType),
            BindingFlags.Instance | BindingFlags.Public);

        await Assert.That(update?.IsPublic ?? true).IsFalse();
        await Assert.That(delete?.IsPublic ?? true).IsFalse();
        await Assert.That(aggregateUpdate?.IsPublic ?? false).IsTrue();
        await Assert.That(aggregateDelete?.IsPublic ?? false).IsTrue();
    }

    [Test]
    public async Task DeleteTicketType_DelegatesExplicitIdempotentDeletion()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = AddFreeTicketWithEventEntitlement(catalog);
        DateTime deletedAt = new(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);
        Guid deletedBy = Guid.CreateVersion7();

        catalog.DeleteTicketType(ticketType, deletedAt, deletedBy);
        catalog.DeleteTicketType(ticketType, deletedAt.AddMinutes(1), Guid.CreateVersion7());

        await Assert.That(ticketType.IsDeleted).IsTrue();
        await Assert.That(ticketType.DeletedAt).IsEqualTo(deletedAt);
        await Assert.That(ticketType.DeletedBy).IsEqualTo(deletedBy);
        await Assert.That(ticketType.UpdatedAt).IsEqualTo(deletedAt);
        await Assert.That(ticketType.UpdatedBy).IsEqualTo(deletedBy);
    }

    [Test]
    public async Task TicketAndCapacityPoolDelete_RejectInvalidAuditArguments()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = AddFreeTicketWithEventEntitlement(catalog);
        EventCapacityPool pool = EventCapacityPool.Create(
            catalog.TenantId,
            catalog.EventId,
            "Main hall",
            200,
            900,
            CapacityHoldPolicyEnum.TimedHoldOnSelection,
            CapacityOversellPolicyEnum.Disallow,
            true);

        await Assert.That(() => catalog.DeleteTicketType(ticketType, default, Guid.CreateVersion7())).Throws<ArgumentException>();
        await Assert.That(() => catalog.DeleteTicketType(ticketType, DomainTestClock.UtcNow, Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => pool.Delete(default, Guid.CreateVersion7())).Throws<ArgumentException>();
        await Assert.That(() => pool.Delete(DomainTestClock.UtcNow, Guid.Empty)).Throws<ArgumentException>();
    }

    [Test]
    public async Task AddTicketType_WhenCurrencyDiffersFromCatalog_RejectsIt()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType euroTicket = CreateTicket(catalog, "EUR", TicketPricingModeEnum.Free, null, null, null);

        await Assert.That(() => catalog.AddTicketType(euroTicket, null)).Throws<ArgumentException>();
    }

    [Test]
    public async Task XxxCurrency_AllowsOnlyFreeTicketCatalogs()
    {
        EventTicketCatalogVersion freeCatalog = CreateCatalog("XXX");
        EventTicketType freeTicket = CreateTicket(freeCatalog, "XXX", TicketPricingModeEnum.Free, null, null, null);
        freeCatalog.AddTicketType(freeTicket, null);
        freeCatalog.AddEntitlement(freeTicket, CreateEventEntitlement(freeCatalog, freeTicket));

        freeCatalog.Publish();

        EventTicketCatalogVersion pricedCatalog = CreateCatalog("XXX");
        await Assert.That(() => CreateTicket(pricedCatalog, "XXX", TicketPricingModeEnum.Donation, null, 0, null))
            .Throws<ArgumentException>();
        await Assert.That(freeCatalog.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Published);
    }

    [Test]
    public async Task AddTicketType_AllowsSharedEventPoolButRejectsCrossEventPool()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventCapacityPool sharedPool = EventCapacityPool.Create(
            catalog.TenantId,
            catalog.EventId,
            "Main hall",
            200,
            900,
            CapacityHoldPolicyEnum.TimedHoldOnSelection,
            CapacityOversellPolicyEnum.Disallow,
            true);
        EventTicketType adultTicket = CreateTicket(catalog, "USD", TicketPricingModeEnum.Free, null, null, null);
        EventTicketType childTicket = CreateTicket(catalog, "USD", TicketPricingModeEnum.Free, null, null, null);

        catalog.AddTicketType(adultTicket, sharedPool);
        catalog.AddTicketType(childTicket, sharedPool);

        EventCapacityPool otherEventPool = EventCapacityPool.Create(
            catalog.TenantId,
            Guid.CreateVersion7(),
            "Other hall",
            100,
            900,
            CapacityHoldPolicyEnum.TimedHoldOnSelection,
            CapacityOversellPolicyEnum.Disallow,
            true);
        EventTicketType otherTicket = CreateTicket(catalog, "USD", TicketPricingModeEnum.Free, null, null, null);

        await Assert.That(adultTicket.CapacityPoolId).IsEqualTo(sharedPool.Id);
        await Assert.That(childTicket.CapacityPoolId).IsEqualTo(sharedPool.Id);
        await Assert.That(() => catalog.AddTicketType(otherTicket, otherEventPool)).Throws<ArgumentException>();
    }

    [Test]
    public async Task AddEntitlement_RejectsCrossEventTargetsAndInvalidSelectionRules()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = CreateTicket(catalog, "USD", TicketPricingModeEnum.Free, null, null, null);
        catalog.AddTicketType(ticketType, null);

        TicketTypeEntitlement crossEventEntitlement = TicketTypeEntitlement.CreateForEvent(
            ticketType.Id,
            catalog.TenantId,
            Guid.CreateVersion7(),
            1);
        EventSession eventSession = CreateSession(catalog.EventId, catalog.TenantId);

        await Assert.That(() => catalog.AddEntitlement(ticketType, crossEventEntitlement)).Throws<ArgumentException>();
        await Assert.That(() => TicketTypeEntitlement.CreateForEventSession(ticketType.Id, eventSession, 2, EntitlementSelectionRuleEnum.ChooseOne))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Entitlements_RejectDuplicateCanonicalTargetScopesOnAddAndReplacement()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = CreateTicket(catalog, "USD", TicketPricingModeEnum.Free, null, null, null);
        catalog.AddTicketType(ticketType, null);
        TicketTypeEntitlement first = TicketTypeEntitlement.CreateForEvent(
            ticketType.Id, catalog.TenantId, catalog.EventId, 1);
        TicketTypeEntitlement duplicate = TicketTypeEntitlement.CreateForEvent(
            ticketType.Id, catalog.TenantId, catalog.EventId, 2);

        catalog.AddEntitlement(ticketType, first);

        await Assert.That(() => catalog.AddEntitlement(ticketType, duplicate)).Throws<ArgumentException>();
        await Assert.That(() => catalog.UpdateTicketType(
                ticketType,
                ticketType.Name,
                TicketPricingModeEnum.Free,
                null,
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
                null,
                [first, duplicate]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task EntitlementFactories_KeepDayAndSessionTargetsInTheCatalogEvent()
    {
        EventTicketCatalogVersion catalog = CreateCatalog();
        EventTicketType ticketType = CreateTicket(catalog, "USD", TicketPricingModeEnum.Free, null, null, null);
        catalog.AddTicketType(ticketType, null);

        TicketTypeEntitlement eventDayEntitlement = TicketTypeEntitlement.CreateForEventDay(
            ticketType.Id,
            CreateDay(catalog.EventId, catalog.TenantId),
            1,
            EntitlementSelectionRuleEnum.FixedSelection);
        TicketTypeEntitlement eventSessionEntitlement = TicketTypeEntitlement.CreateForEventSession(
            ticketType.Id,
            CreateSession(catalog.EventId, catalog.TenantId),
            2,
            EntitlementSelectionRuleEnum.ChooseUpToN);
        TicketTypeEntitlement foreignSessionEntitlement = TicketTypeEntitlement.CreateForEventSession(
            ticketType.Id,
            CreateSession(Guid.CreateVersion7(), catalog.TenantId),
            1,
            EntitlementSelectionRuleEnum.FixedSelection);

        catalog.AddEntitlement(ticketType, eventDayEntitlement);
        catalog.AddEntitlement(ticketType, eventSessionEntitlement);

        await Assert.That(ticketType.Entitlements.Count).IsEqualTo(2);
        await Assert.That(() => catalog.AddEntitlement(ticketType, foreignSessionEntitlement)).Throws<ArgumentException>();
        await Assert.That(() => TicketTypeEntitlement.CreateForEvent(ticketType.Id, catalog.TenantId, catalog.EventId, 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static EventTicketCatalogVersion CreateCatalog(string currencyCode = "USD") => EventTicketCatalogVersion.Create(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        currencyCode,
        1);

    private static EventTicketType AddFreeTicketWithEventEntitlement(EventTicketCatalogVersion catalog)
    {
        EventTicketType ticketType = CreateTicket(catalog, "USD", TicketPricingModeEnum.Free, null, null, null);
        catalog.AddTicketType(ticketType, null);
        catalog.AddEntitlement(ticketType, CreateEventEntitlement(catalog, ticketType));
        return ticketType;
    }

    private static EventTicketType CreateTicket(
        EventTicketCatalogVersion catalog,
        string currencyCode,
        TicketPricingModeEnum pricingMode,
        long? fixedPriceMinor,
        long? minimumPriceMinor,
        long? suggestedPriceMinor) => EventTicketType.Create(
            Guid.CreateVersion7(),
        catalog.TenantId,
        catalog.Id,
        "General admission",
        currencyCode,
        pricingMode,
        CreateMoney(fixedPriceMinor, currencyCode),
        CreateMoney(minimumPriceMinor, currencyCode),
        CreateMoney(suggestedPriceMinor, currencyCode),
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

    private static TicketTypeEntitlement CreateEventEntitlement(EventTicketCatalogVersion catalog, EventTicketType ticketType) => TicketTypeEntitlement.CreateForEvent(
        ticketType.Id,
        catalog.TenantId,
        catalog.EventId,
        1);

    private static Money? CreateMoney(long? minorUnits, string currencyCode) =>
        minorUnits.HasValue ? Money.Create(minorUnits.Value, currencyCode) : null;

    private static EventSession CreateSession(Guid eventId, Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        EventId = eventId,
        TenantId = tenantId,
        Event = null!,
        Tenant = null!
    };

    private static EventDay CreateDay(Guid eventId, Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        EventId = eventId,
        TenantId = tenantId,
        Event = null!,
        Tenant = null!
    };
}
