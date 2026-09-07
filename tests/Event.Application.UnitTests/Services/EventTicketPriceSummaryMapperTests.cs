using System.Reflection;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Domain.Enums;
using DomainEvent = Explore.Domain.Event;

namespace Event.Application.UnitTests.Services;

public sealed class EventTicketPriceSummaryMapperTests
{
    [Test]
    public async Task Map_PublishedFixedCatalog_ReturnsFixedSummary()
    {
        DomainEvent @event = CreatePlatformEvent();
        EventTicketCatalogVersion catalog = CreateCatalog(
            @event,
            "USD",
            publish: true,
            (TicketPricingModeEnum.Fixed, 2_400, null, null),
            (TicketPricingModeEnum.Fixed, 1_250, null, null));
        AttachCatalog(@event, catalog);

        var summary = EventTicketPriceSummaryMapper.Map(@event);

        await Assert.That(summary?.SummaryCode).IsEqualTo("FIXED");
        await Assert.That(summary?.CurrencyCode).IsEqualTo("USD");
        await Assert.That(summary?.CurrencyMinorUnitDigits).IsEqualTo(2);
        await Assert.That(summary?.FromAmountMinor).IsEqualTo(1_250);
    }

    [Test]
    public async Task Map_AllFreeCatalogWithNoCurrency_ReturnsFreeSummary()
    {
        DomainEvent @event = CreatePlatformEvent();
        AttachCatalog(@event, CreateCatalog(
            @event,
            "XXX",
            publish: true,
            (TicketPricingModeEnum.Free, null, null, null)));

        var summary = EventTicketPriceSummaryMapper.Map(@event);

        await Assert.That(summary?.SummaryCode).IsEqualTo("FREE");
        await Assert.That(summary?.CurrencyCode).IsNull();
        await Assert.That(summary?.CurrencyMinorUnitDigits).IsEqualTo(0);
        await Assert.That(summary?.FromAmountMinor).IsEqualTo(0);
    }

    [Test]
    [Arguments(TicketPricingModeEnum.Donation, "DONATION", 0L, null)]
    [Arguments(TicketPricingModeEnum.PayWhatYouCan, "PAY_WHAT_YOU_CAN", 300L, 900L)]
    [Arguments(TicketPricingModeEnum.SlidingScale, "SLIDING_SCALE", 600L, 1_200L)]
    public async Task Map_HomogeneousBuyerPricedCatalog_ReturnsModeMinimum(
        TicketPricingModeEnum mode,
        string expectedCode,
        long minimumPriceMinor,
        long? suggestedPriceMinor)
    {
        DomainEvent @event = CreatePlatformEvent();
        AttachCatalog(@event, CreateCatalog(
            @event,
            "EUR",
            publish: true,
            (mode, null, minimumPriceMinor, suggestedPriceMinor),
            (mode, null, minimumPriceMinor + 200, suggestedPriceMinor.HasValue ? suggestedPriceMinor + 200 : null)));

        var summary = EventTicketPriceSummaryMapper.Map(@event);

        await Assert.That(summary?.SummaryCode).IsEqualTo(expectedCode);
        await Assert.That(summary?.FromAmountMinor).IsEqualTo(minimumPriceMinor);
    }

    [Test]
    public async Task Map_MixedCatalogWithFreeTicket_ReturnsMixedWithFreeSummary()
    {
        DomainEvent @event = CreatePlatformEvent();
        EventTicketCatalogVersion catalog = CreateCatalog(
            @event,
            "EUR",
            publish: true,
            (TicketPricingModeEnum.Free, null, null, null),
            (TicketPricingModeEnum.Fixed, 900, null, null));
        AttachCatalog(@event, catalog);

        var summary = EventTicketPriceSummaryMapper.Map(@event);

        await Assert.That(summary?.SummaryCode).IsEqualTo("MIXED_WITH_FREE");
        await Assert.That(summary?.FromAmountMinor).IsEqualTo(0);
    }

    [Test]
    public async Task Map_MixedCatalogWithoutFreeTicketAndZeroMinimum_ReturnsMixedZeroSummary()
    {
        DomainEvent @event = CreatePlatformEvent();
        AttachCatalog(@event, CreateCatalog(
            @event,
            "EUR",
            publish: true,
            (TicketPricingModeEnum.Donation, null, 0, null),
            (TicketPricingModeEnum.Fixed, 900, null, null)));

        var summary = EventTicketPriceSummaryMapper.Map(@event);

        await Assert.That(summary?.SummaryCode).IsEqualTo("MIXED");
        await Assert.That(summary?.FromAmountMinor).IsEqualTo(0);
    }

    [Test]
    public async Task Map_MixedPositiveCatalog_ReturnsMixedMinimumSummary()
    {
        DomainEvent @event = CreatePlatformEvent();
        AttachCatalog(@event, CreateCatalog(
            @event,
            "EUR",
            publish: true,
            (TicketPricingModeEnum.Donation, null, 400, null),
            (TicketPricingModeEnum.Fixed, 900, null, null)));

        var summary = EventTicketPriceSummaryMapper.Map(@event);

        await Assert.That(summary?.SummaryCode).IsEqualTo("MIXED");
        await Assert.That(summary?.FromAmountMinor).IsEqualTo(400);
    }

    [Test]
    public async Task Map_ExternalManagedEvent_ReturnsNoSummary()
    {
        DomainEvent @event = CreatePlatformEvent(ParticipationHandlingModeEnum.ExternalManaged);
        AttachCatalog(@event, CreateCatalog(
            @event,
            "USD",
            publish: true,
            (TicketPricingModeEnum.Free, null, null, null)));

        await Assert.That(EventTicketPriceSummaryMapper.Map(@event)).IsNull();
    }

    [Test]
    public async Task Map_MissingPublishedCatalogOrSelectableTypes_ReturnsNoSummary()
    {
        DomainEvent noCatalog = CreatePlatformEvent();
        DomainEvent draftCatalog = CreatePlatformEvent();
        AttachCatalog(draftCatalog, CreateCatalog(
            draftCatalog,
            "USD",
            publish: false,
            (TicketPricingModeEnum.Free, null, null, null)));
        DomainEvent deletedType = CreatePlatformEvent();
        EventTicketCatalogVersion published = CreateCatalog(
            deletedType,
            "USD",
            publish: true,
            (TicketPricingModeEnum.Free, null, null, null));
        published.TicketTypes.Single().IsDeleted = true;
        AttachCatalog(deletedType, published);

        await Assert.That(EventTicketPriceSummaryMapper.Map(noCatalog)).IsNull();
        await Assert.That(EventTicketPriceSummaryMapper.Map(draftCatalog)).IsNull();
        await Assert.That(EventTicketPriceSummaryMapper.Map(deletedType)).IsNull();
    }

    private static DomainEvent CreatePlatformEvent(ParticipationHandlingModeEnum mode = ParticipationHandlingModeEnum.PlatformManaged)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        return new DomainEvent
        {
            Id = eventId,
            TenantId = tenantId,
            Title = "Ticketing event",
            Actor = null!,
            Tenant = null!,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormat = null!,
            ParticipationConfiguration = EventParticipationConfiguration.Create(
                eventId,
                tenantId,
                (int)mode,
                (int)AdvanceRegistrationObligationEnum.Required,
                mode == ParticipationHandlingModeEnum.PlatformManaged ? (int)IdentityAccessModeEnum.AccountRequired : null,
                guestRecoveryPolicy: null,
                DateTime.UtcNow)
        };
    }

    private static EventTicketCatalogVersion CreateCatalog(
        DomainEvent @event,
        string currencyCode,
        bool publish,
        params (TicketPricingModeEnum Mode, long? FixedPriceMinor, long? MinimumPriceMinor, long? SuggestedPriceMinor)[] pricing)
    {
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(@event.TenantId, @event.Id, currencyCode, 1);
        foreach ((TicketPricingModeEnum mode, long? fixedPriceMinor, long? minimumPriceMinor, long? suggestedPriceMinor) in pricing)
        {
            EventTicketType ticketType = EventTicketType.Create(
                Guid.CreateVersion7(),
                    catalog.TenantId,
                    catalog.Id,
                    "General admission",
                    currencyCode,
                    mode,
                    fixedPriceMinor.HasValue ? Money.Create(fixedPriceMinor.Value, currencyCode) : null,
                    minimumPriceMinor.HasValue ? Money.Create(minimumPriceMinor.Value, currencyCode) : null,
                    suggestedPriceMinor.HasValue ? Money.Create(suggestedPriceMinor.Value, currencyCode) : null,
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

        if (publish)
        {
            catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
            catalog.Publish();
        }

        return catalog;
    }

    private static void AttachCatalog(DomainEvent @event, EventTicketCatalogVersion catalog)
    {
        var catalogs = (List<EventTicketCatalogVersion>)typeof(DomainEvent)
            .GetField("_ticketCatalogVersions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(@event)!;
        catalogs.Add(catalog);
    }
}
