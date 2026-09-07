using System.Reflection;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Domain.Enums;
using DomainEvent = Explore.Domain.Event;

namespace Event.Application.UnitTests.Specifications;

public sealed class EventFilterTests
{
    [Test]
    [Arguments(TicketPricingModeEnum.Free, null, true)]
    [Arguments(TicketPricingModeEnum.Fixed, 100L, false)]
    [Arguments(TicketPricingModeEnum.Donation, null, true)]
    [Arguments(TicketPricingModeEnum.Donation, 0L, true)]
    [Arguments(TicketPricingModeEnum.Donation, 1L, false)]
    [Arguments(TicketPricingModeEnum.PayWhatYouCan, null, true)]
    [Arguments(TicketPricingModeEnum.PayWhatYouCan, 0L, true)]
    [Arguments(TicketPricingModeEnum.PayWhatYouCan, 1L, false)]
    [Arguments(TicketPricingModeEnum.SlidingScale, 0L, true)]
    [Arguments(TicketPricingModeEnum.SlidingScale, 1L, false)]
    public async Task Free_SelectablePricingMatrix_MatchesExpected(
        TicketPricingModeEnum mode,
        long? amountMinor,
        bool expected)
    {
        DomainEvent @event = CreateEvent(ParticipationHandlingModeEnum.PlatformManaged);
        AttachCatalog(@event, CreateCatalog(@event, mode, amountMinor, CatalogState.Published));

        bool result = EventFilter.Free().Predicate.Compile()(@event);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Free_InactiveOrOutOfScopeCatalogsAndTickets_DoNotMatch()
    {
        DomainEvent external = CreateEvent(ParticipationHandlingModeEnum.ExternalManaged);
        AttachCatalog(external, CreateCatalog(external, TicketPricingModeEnum.Free, null, CatalogState.Published));

        DomainEvent draft = CreateEvent(ParticipationHandlingModeEnum.PlatformManaged);
        AttachCatalog(draft, CreateCatalog(draft, TicketPricingModeEnum.Free, null, CatalogState.Draft));

        DomainEvent retired = CreateEvent(ParticipationHandlingModeEnum.PlatformManaged);
        AttachCatalog(retired, CreateCatalog(retired, TicketPricingModeEnum.Free, null, CatalogState.Retired));

        DomainEvent deletedCatalog = CreateEvent(ParticipationHandlingModeEnum.PlatformManaged);
        EventTicketCatalogVersion deletedCatalogVersion = CreateCatalog(
            deletedCatalog,
            TicketPricingModeEnum.Free,
            null,
            CatalogState.Published);
        deletedCatalogVersion.IsDeleted = true;
        AttachCatalog(deletedCatalog, deletedCatalogVersion);

        DomainEvent deletedTicket = CreateEvent(ParticipationHandlingModeEnum.PlatformManaged);
        EventTicketCatalogVersion deletedTicketCatalog = CreateCatalog(
            deletedTicket,
            TicketPricingModeEnum.Free,
            null,
            CatalogState.Published);
        deletedTicketCatalog.TicketTypes.Single().IsDeleted = true;
        AttachCatalog(deletedTicket, deletedTicketCatalog);

        var predicate = EventFilter.Free().Predicate.Compile();
        await Assert.That(predicate(external)).IsFalse();
        await Assert.That(predicate(draft)).IsFalse();
        await Assert.That(predicate(retired)).IsFalse();
        await Assert.That(predicate(deletedCatalog)).IsFalse();
        await Assert.That(predicate(deletedTicket)).IsFalse();
    }

    private static DomainEvent CreateEvent(ParticipationHandlingModeEnum mode)
    {
        Guid id = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        return new DomainEvent
        {
            Id = id,
            TenantId = tenantId,
            Title = "Filter event",
            Actor = null!,
            Tenant = null!,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormat = null!,
            ParticipationConfiguration = EventParticipationConfiguration.Create(
                id,
                tenantId,
                (int)mode,
                mode == ParticipationHandlingModeEnum.PlatformManaged
                    ? (int)AdvanceRegistrationObligationEnum.Required
                    : (int)AdvanceRegistrationObligationEnum.Optional,
                mode == ParticipationHandlingModeEnum.PlatformManaged
                    ? (int)IdentityAccessModeEnum.AccountRequired
                    : null,
                guestRecoveryPolicy: null,
                DateTime.UtcNow)
        };
    }

    private static EventTicketCatalogVersion CreateCatalog(
        DomainEvent @event,
        TicketPricingModeEnum mode,
        long? amountMinor,
        CatalogState state)
    {
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(@event.TenantId, @event.Id, "USD", 1);
        EventTicketType ticketType = EventTicketType.Create(
            Guid.CreateVersion7(),
            catalog.TenantId,
            catalog.Id,
            "General admission",
            "USD",
            mode,
            mode == TicketPricingModeEnum.Fixed && amountMinor.HasValue ? Money.Create(amountMinor.Value, "USD") : null,
            mode is TicketPricingModeEnum.Donation or TicketPricingModeEnum.PayWhatYouCan or TicketPricingModeEnum.SlidingScale
                && amountMinor.HasValue
                ? Money.Create(amountMinor.Value, "USD")
                : null,
            mode == TicketPricingModeEnum.SlidingScale ? Money.Create(amountMinor.GetValueOrDefault() + 100, "USD") : null,
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
        catalog.AddEntitlement(ticketType, TicketTypeEntitlement.CreateForEvent(
            ticketType.Id,
            catalog.TenantId,
            catalog.EventId,
            1));

        if (state != CatalogState.Draft)
        {
            catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
            catalog.Publish();
        }

        if (state == CatalogState.Retired)
        {
            catalog.Retire();
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

    private enum CatalogState
    {
        Draft,
        Published,
        Retired
    }
}
