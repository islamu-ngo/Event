// ABOUTME: Persistence regression tests for EventSeries nested event graph loading.
// ABOUTME: Proves published ticket pricing reaches the application summary mapper without Docker.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventSeriesRepositoryTests
{
    [Test]
    public async Task EventSeriesGraphs_LoadPublishedTicketPricingForNestedEvents()
    {
        await using var context = CreateInMemoryContext();
        Guid tenantId = Guid.CreateVersion7();
        Guid seriesId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;
        Actor actor = new()
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = 1,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Ticketed Actor" }
        };

        EventSeries series = new()
        {
            Id = seriesId,
            Title = "Ticketed Series",
            ActorId = actor.Id,
            Actor = actor,
            IsPublished = true,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        DomainEvent @event = new(EventStatusEnum.Published)
        {
            Id = eventId,
            Title = "Ticketed Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = actor,
            TenantId = tenantId,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventSeriesId = seriesId,
            EventSeries = series,
            LastSessionEndUtc = DateTimeOffset.UtcNow.AddDays(1),
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        EventParticipationConfiguration participationConfiguration = EventParticipationConfiguration.Create(
            eventId,
            tenantId,
            (int)ParticipationHandlingModeEnum.PlatformManaged,
            (int)AdvanceRegistrationObligationEnum.Optional,
            identityAccessModeId: (int)IdentityAccessModeEnum.AccountRequired,
            guestRecoveryPolicy: null,
            now);
        @event.ParticipationConfiguration = participationConfiguration;

        EventTicketCatalogVersion publishedCatalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 2);
        publishedCatalog.CreatedAt = now;
        EventTicketType activeTicket = EventTicketType.Create(
            Guid.CreateVersion7(),
            tenantId,
            publishedCatalog.Id,
            "General",
            "USD",
            TicketPricingModeEnum.Fixed,
            fixedPrice: Money.Create(2500, "USD"),
            minimumPrice: null,
            suggestedPrice: null,
            ParticipantDataCollectionModeEnum.None,
            capacityPoolId: null,
            minimumAge: null,
            maximumAge: null,
            requiresGuardian: false,
            requiresApproval: false,
            perOrderLimit: null,
            perAccountLimit: null,
            perVerifiedContactLimit: null,
            perBookingPartyLimit: null);
        activeTicket.CreatedAt = now;
        publishedCatalog.AddTicketType(activeTicket, capacityPool: null);
        publishedCatalog.AddEntitlement(activeTicket, TicketTypeEntitlement.CreateForEvent(activeTicket.Id, tenantId, eventId, 1));

        EventTicketType deletedTicket = EventTicketType.Create(
            Guid.CreateVersion7(),
            tenantId,
            publishedCatalog.Id,
            "Deleted",
            "USD",
            TicketPricingModeEnum.Fixed,
            fixedPrice: Money.Create(9900, "USD"),
            minimumPrice: null,
            suggestedPrice: null,
            ParticipantDataCollectionModeEnum.None,
            capacityPoolId: null,
            minimumAge: null,
            maximumAge: null,
            requiresGuardian: false,
            requiresApproval: false,
            perOrderLimit: null,
            perAccountLimit: null,
            perVerifiedContactLimit: null,
            perBookingPartyLimit: null);
        deletedTicket.CreatedAt = now;
        deletedTicket.IsDeleted = true;
        publishedCatalog.AddTicketType(deletedTicket, capacityPool: null);
        publishedCatalog.AddEntitlement(deletedTicket, TicketTypeEntitlement.CreateForEvent(deletedTicket.Id, tenantId, eventId, 1));
        publishedCatalog.UpdateCommercialDisclosures(
            "Tickets sold by the series organizer.",
            "Refund requests are reviewed by the organizer before the event.",
            "Contact tickets@example.com for ticket support.");
        publishedCatalog.Publish();

        EventTicketCatalogVersion draftCatalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        draftCatalog.CreatedAt = now;

        context.EnableTenantFilterBypass("Seeds EventSeries pricing graph regression test rows.");
        context.AddRange(actor, series, @event, participationConfiguration, publishedCatalog, draftCatalog);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        context.TenantContext = new TestTenantContext(tenantId);
        context.EnableTenantFilterBypass("Loads EventSeries pricing graph rows through explicit include filters.");

        var repository = new EventSeriesRepository(context);
        EventSeries loadedDetail = (await repository.GetEventSeriesWithEvents(seriesId))!;
        EventSeries loadedTop = (await repository.GetTopEventSeries(DateTimeOffset.UtcNow))!;

        await AssertLoadedPricingAsync(loadedDetail.Events.Single());
        await AssertLoadedPricingAsync(loadedTop.Events.Single());
    }

    private static async Task AssertLoadedPricingAsync(DomainEvent @event)
    {
        await Assert.That(@event.ParticipationConfiguration).IsNotNull();
        await Assert.That(@event.TicketCatalogVersions.Count).IsEqualTo(1);
        await Assert.That(@event.TicketCatalogVersions.Single().TicketTypes.Count).IsEqualTo(1);

        var summary = EventTicketPriceSummaryMapper.Map(@event);
        await Assert.That(summary).IsNotNull();
        await Assert.That(summary!.SummaryCode).IsEqualTo("FIXED");
        await Assert.That(summary.CurrencyCode).IsEqualTo("USD");
        await Assert.That(summary.FromAmountMinor).IsEqualTo(2500L);
    }

    private static EventSeriesTestDbContext CreateInMemoryContext() =>
        new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"event-series-pricing-{Guid.CreateVersion7():N}")
            .Options);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed class EventSeriesTestDbContext(DbContextOptions<ExploreDbContext> options)
        : ExploreDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Actor>()
                .Ignore(actor => actor.MergesFrom)
                .Ignore(actor => actor.MergesInto);
            modelBuilder.Ignore<ActorMerge>();
        }
    }
}
