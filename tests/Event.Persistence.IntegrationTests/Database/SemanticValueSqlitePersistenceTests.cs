// ABOUTME: SQLite round-trip tests for semantic money, coordinate, and schedule values over scalar EF columns.
// ABOUTME: Keeps existing UTC/payment checks green while specifying four new named database invariants in RED.

using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class SemanticValueSqlitePersistenceTests
{
    private static readonly DateTime CreatedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task MoneyValues_RoundTripNullableTicketPricesAndRequiredPaymentComposition()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        Guid tenantId = Guid.CreateVersion7();
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, Guid.CreateVersion7(), "EUR", 1);
        EventTicketType free = CreateTicket(catalog, "Free", TicketPricingModeEnum.Free, null, null, null);
        EventTicketType fixedPrice = CreateTicket(catalog, "Fixed", TicketPricingModeEnum.Fixed, Money.Create(2_500, "EUR"), null, null);
        EventTicketType sliding = CreateTicket(catalog, "Sliding", TicketPricingModeEnum.SlidingScale, null, Money.Create(1_000, "EUR"), Money.Create(1_750, "EUR"));
        PaymentAttempt payment = CreatePaymentAttempt(tenantId);
        catalog.AddTicketType(free, null);
        catalog.AddTicketType(fixedPrice, null);
        catalog.AddTicketType(sliding, null);
        context.AddRange(catalog, payment);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        EventTicketType[] tickets = await context.Set<EventTicketType>().AsNoTracking().ToArrayAsync();
        PaymentAttempt reloadedPayment = await context.Set<PaymentAttempt>().AsNoTracking().SingleAsync();
        EventTicketType reloadedFree = tickets.Single(ticket => ticket.Name == "Free");
        EventTicketType reloadedFixed = tickets.Single(ticket => ticket.Name == "Fixed");
        EventTicketType reloadedSliding = tickets.Single(ticket => ticket.Name == "Sliding");

        await Assert.That(reloadedFree.FixedPriceMinor).IsNull();
        await Assert.That(reloadedFree.MinimumPriceMinor).IsNull();
        await Assert.That(reloadedFree.SuggestedPriceMinor).IsNull();
        await Assert.That(reloadedFixed.FixedPriceMinor).IsEqualTo(2_500);
        await Assert.That(reloadedSliding.MinimumPriceMinor).IsEqualTo(1_000);
        await Assert.That(reloadedSliding.SuggestedPriceMinor).IsEqualTo(1_750);
        await Assert.That(reloadedPayment.CurrencyCode).IsEqualTo("EUR");
        await Assert.That(reloadedPayment.OrganizerAmountMinor).IsEqualTo(1_000);
        await Assert.That(reloadedPayment.PlatformFeeMinor).IsEqualTo(75);
        await Assert.That(reloadedPayment.PlatformContributionMinor).IsEqualTo(125);
        await Assert.That(reloadedPayment.TotalMinor).IsEqualTo(1_125);
    }

    [Test]
    public async Task Coordinates_RoundTripAbsentAndPresentPairsThroughLocationPiiApi()
    {
        Location absent = CreateLocation("No coordinate street", "1000", null);
        LocationPii reloadedAbsent;
        await using (ExploreDbContext absentContext = await CreateContextAsync())
        {
            absentContext.Add(absent);
            await absentContext.SaveChangesAsync();
            absentContext.ChangeTracker.Clear();
            reloadedAbsent = (await absentContext.Set<Location>().AsNoTracking().SingleAsync()).Pii!;
        }

        GeoCoordinate expected = GeoCoordinate.Create(50.8503, 4.3517);
        Location present = CreateLocation("Coordinate street", "1000", expected);
        await using ExploreDbContext presentContext = await CreateContextAsync();
        presentContext.Add(present);
        await presentContext.SaveChangesAsync();
        presentContext.ChangeTracker.Clear();
        LocationPii reloadedPresent = (await presentContext.Set<Location>().AsNoTracking().SingleAsync()).Pii!;

        await Assert.That(reloadedAbsent.GetCoordinate()).IsNull();
        await Assert.That(reloadedPresent.GetCoordinate()).IsEqualTo(expected);
    }

    [Test]
    public async Task AgendaSchedule_RoundTripsUtcAndLocalRangesThroughSemanticApis()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        UtcInstantRange utcRange = UtcInstantRange.Create(
            new DateTimeOffset(2026, 8, 25, 22, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 26, 1, 0, 0, TimeSpan.Zero));
        EventAgendaItem agendaItem = CreateAgendaItem();
        agendaItem.Reschedule(utcRange, "Europe/Brussels", new EventScheduleProjectionCalculator());
        context.Add(agendaItem);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        EventAgendaItem reloaded = await context.Set<EventAgendaItem>().AsNoTracking().SingleAsync();
        await Assert.That(reloaded.GetUtcSchedule()).IsEqualTo(utcRange);
        await Assert.That(reloaded.GetLocalDateRange()).IsEqualTo(
            LocalDateRange.Create(new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 26)));
    }

    [Test]
    public async Task SessionSchedules_RoundTripUnscheduledFixedOpenEndedAndPrayerRelativeStates()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        var calculator = new EventScheduleProjectionCalculator();
        EventSession unscheduled = CreateSession("Unscheduled");
        unscheduled.Unschedule();
        EventSession fixedSchedule = CreateSession("Fixed");
        UtcInstantRange fixedRange = UtcInstantRange.Create(
            new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 25, 9, 30, 0, TimeSpan.Zero));
        fixedSchedule.Reschedule(fixedRange, "Europe/Brussels", calculator);
        EventSession openEnded = CreateSession("Open ended");
        openEnded.ScheduleOpenEnded(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero), "Europe/Brussels", calculator);
        EventSession prayerRelative = CreateSession("Prayer relative");
        UtcInstantRange prayerRange = UtcInstantRange.Create(
            new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 25, 13, 0, 0, TimeSpan.Zero));
        prayerRelative.ScheduleRelativeToPrayer(prayerRange, "Europe/Brussels", calculator);
        context.AddRange(unscheduled, fixedSchedule, openEnded, prayerRelative);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        EventSession[] sessions = await context.Set<EventSession>().AsNoTracking().ToArrayAsync();
        EventSession reloadedUnscheduled = sessions.Single(session => session.Title == "Unscheduled");
        EventSession reloadedFixed = sessions.Single(session => session.Title == "Fixed");
        EventSession reloadedOpenEnded = sessions.Single(session => session.Title == "Open ended");
        EventSession reloadedPrayer = sessions.Single(session => session.Title == "Prayer relative");
        await Assert.That(reloadedUnscheduled.GetUtcSchedule()).IsNull();
        await Assert.That(reloadedUnscheduled.GetLocalDateRange()).IsNull();
        await Assert.That(reloadedFixed.EndTimeType).IsEqualTo(SessionEndTimeType.Fixed);
        await Assert.That(reloadedFixed.GetUtcSchedule()).IsEqualTo(fixedRange);
        await Assert.That(reloadedFixed.GetLocalDateRange()).IsEqualTo(LocalDateRange.Create(new(2026, 8, 25), new(2026, 8, 25)));
        await Assert.That(reloadedOpenEnded.EndTimeType).IsEqualTo(SessionEndTimeType.OpenEnded);
        await Assert.That(reloadedOpenEnded.StartTime).IsNotNull();
        await Assert.That(reloadedOpenEnded.GetUtcSchedule()).IsNull();
        await Assert.That(reloadedOpenEnded.GetLocalDateRange()).IsNull();
        await Assert.That(reloadedPrayer.EndTimeType).IsEqualTo(SessionEndTimeType.RelativeToPrayer);
        await Assert.That(reloadedPrayer.GetUtcSchedule()).IsEqualTo(prayerRange);
        await Assert.That(reloadedPrayer.GetLocalDateRange()).IsEqualTo(LocalDateRange.Create(new(2026, 8, 25), new(2026, 8, 25)));
    }

    [Test]
    [Arguments("ck_event_ticket_type_money_nonnegative", InvariantMutation.NegativeTicketAmount)]
    [Arguments("ck_event_ticket_type_money_nonnegative", InvariantMutation.NegativeMinimumTicketAmount)]
    [Arguments("ck_event_ticket_type_money_nonnegative", InvariantMutation.NegativeSuggestedTicketAmount)]
    [Arguments("ck_location_pii_coordinate_shape", InvariantMutation.PartialCoordinate)]
    [Arguments("ck_location_pii_coordinate_shape", InvariantMutation.PartialCoordinateMissingLatitude)]
    [Arguments("ck_location_pii_coordinate_shape", InvariantMutation.OutOfRangeCoordinate)]
    [Arguments("ck_location_pii_coordinate_shape", InvariantMutation.LatitudeBelowRange)]
    [Arguments("ck_location_pii_coordinate_shape", InvariantMutation.LongitudeAboveRange)]
    [Arguments("ck_location_pii_coordinate_shape", InvariantMutation.LongitudeBelowRange)]
    [Arguments("ck_event_agenda_item_local_date_range", InvariantMutation.ReversedAgendaLocalDates)]
    [Arguments("ck_event_session_local_date_range", InvariantMutation.ReversedSessionLocalDates)]
    [Arguments("ck_event_agenda_item_end_after_start", InvariantMutation.ReversedAgendaUtcRange)]
    [Arguments("ck_event_session_end_after_start", InvariantMutation.ReversedSessionUtcRange)]
    [Arguments("ck_payment_attempts_amounts", InvariantMutation.InvalidPaymentComposition)]
    public async Task NamedChecks_RejectDirectSqlInvariantBreakers(string expectedConstraint, InvariantMutation mutation)
    {
        await using ExploreDbContext context = await CreateContextAsync();
        await SeedCarrierAsync(context, mutation);

        SqliteException? exception = await Assert.That(() => context.Database.ExecuteSqlRawAsync(MutationSql(mutation)))
            .Throws<SqliteException>();

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.SqliteErrorCode).IsEqualTo(19);
        await Assert.That(exception.SqliteExtendedErrorCode).IsEqualTo(275);
        await Assert.That(exception.Message).Contains(expectedConstraint);
    }

    private static async Task<ExploreDbContext> CreateContextAsync()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite("Data Source=:memory:")
            .UseSnakeCaseNamingConvention()
            .Options;
        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Phase 10 semantic-value SQLite persistence tests.");
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static EventTicketType CreateTicket(
        EventTicketCatalogVersion catalog,
        string name,
        TicketPricingModeEnum mode,
        Money? fixedPrice,
        Money? minimumPrice,
        Money? suggestedPrice) => EventTicketType.Create(
            Guid.CreateVersion7(), catalog.TenantId, catalog.Id, name, catalog.CurrencyCode,
            mode, fixedPrice, minimumPrice, suggestedPrice, ParticipantDataCollectionModeEnum.None,
            null, null, null, false, false, null, null, null, null);

    private static PaymentAttempt CreatePaymentAttempt(Guid tenantId)
    {
        OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
            tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), "stripe", "platform-eu",
            "acct_semantic_values", "BE", "EUR", Guid.CreateVersion7(), null, CreatedAt);
        return PaymentAttempt.Create(
            Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), recipient, "OrganizerDirect",
            "2026-08-25.acacia", "semantic-values-v1", Money.Create(1_000, "EUR"),
            Money.Create(75, "EUR"), Money.Create(125, "EUR"),
            $"semantic-values:{Guid.CreateVersion7():N}", CreatedAt, CreatedAt.AddMinutes(30));
    }

    private static EventAgendaItem CreateAgendaItem() => new()
    {
        Id = Guid.CreateVersion7(), EventId = Guid.CreateVersion7(), Event = null!,
        Title = "Semantic agenda item", TenantId = Guid.CreateVersion7(), Tenant = null!,
        SortOrder = 1, ConcurrencyStamp = Guid.CreateVersion7()
    };

    private static EventSession CreateSession(string title) => new()
    {
        Id = Guid.CreateVersion7(), EventId = Guid.CreateVersion7(), Event = null!, Title = title,
        TenantId = Guid.CreateVersion7(), Tenant = null!, ConcurrencyStamp = Guid.CreateVersion7()
    };

    private static Location CreateLocation(
        string address,
        string postcode,
        GeoCoordinate? coordinate)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Semantic location",
            Country = "BE",
            City = "Brussels",
            TenantId = Guid.CreateVersion7(),
            Tenant = null!,
            CreatedAt = CreatedAt,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        if (coordinate is null)
        {
            location.SetManualAddress(address, postcode);
        }
        else
        {
            location.SetProviderAddress(address, postcode, coordinate);
        }

        return location;
    }

    private static async Task SeedCarrierAsync(ExploreDbContext context, InvariantMutation mutation)
    {
        switch (mutation)
        {
            case InvariantMutation.NegativeTicketAmount:
            case InvariantMutation.NegativeMinimumTicketAmount:
            case InvariantMutation.NegativeSuggestedTicketAmount:
            {
                Guid tenantId = Guid.CreateVersion7();
                EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, Guid.CreateVersion7(), "EUR", 1);
                bool fixedPrice = mutation == InvariantMutation.NegativeTicketAmount;
                EventTicketType ticket = CreateTicket(
                    catalog,
                    "Invariant ticket",
                    fixedPrice ? TicketPricingModeEnum.Fixed : TicketPricingModeEnum.SlidingScale,
                    fixedPrice ? Money.Create(100, "EUR") : null,
                    fixedPrice ? null : Money.Create(50, "EUR"),
                    fixedPrice ? null : Money.Create(75, "EUR"));
                catalog.AddTicketType(ticket, null);
                context.Add(catalog);
                break;
            }
            case InvariantMutation.PartialCoordinate:
            case InvariantMutation.PartialCoordinateMissingLatitude:
            case InvariantMutation.OutOfRangeCoordinate:
            case InvariantMutation.LatitudeBelowRange:
            case InvariantMutation.LongitudeAboveRange:
            case InvariantMutation.LongitudeBelowRange:
                context.Add(CreateLocation(
                    "Invariant coordinate street",
                    "1000",
                    GeoCoordinate.Create(50.8503, 4.3517)));
                break;
            case InvariantMutation.ReversedAgendaLocalDates:
            case InvariantMutation.ReversedAgendaUtcRange:
                EventAgendaItem agendaItem = CreateAgendaItem();
                agendaItem.Reschedule(UtcInstantRange.Create(
                    new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero)), "UTC", new EventScheduleProjectionCalculator());
                context.Add(agendaItem);
                break;
            case InvariantMutation.ReversedSessionLocalDates:
            case InvariantMutation.ReversedSessionUtcRange:
                EventSession session = CreateSession("Invariant session");
                session.Reschedule(UtcInstantRange.Create(
                    new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 25, 11, 0, 0, TimeSpan.Zero)), "UTC", new EventScheduleProjectionCalculator());
                context.Add(session);
                break;
            case InvariantMutation.InvalidPaymentComposition:
                context.Add(CreatePaymentAttempt(Guid.CreateVersion7()));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static string MutationSql(InvariantMutation mutation) => mutation switch
    {
        InvariantMutation.NegativeTicketAmount => "UPDATE ie_event_ticket_types SET fixed_price_minor = -1",
        InvariantMutation.NegativeMinimumTicketAmount => "UPDATE ie_event_ticket_types SET minimum_price_minor = -1",
        InvariantMutation.NegativeSuggestedTicketAmount => "UPDATE ie_event_ticket_types SET suggested_price_minor = -1",
        InvariantMutation.PartialCoordinate => "UPDATE ie_location_pii SET longitude = NULL",
        InvariantMutation.PartialCoordinateMissingLatitude => "UPDATE ie_location_pii SET latitude = NULL",
        InvariantMutation.OutOfRangeCoordinate => "UPDATE ie_location_pii SET latitude = 91",
        InvariantMutation.LatitudeBelowRange => "UPDATE ie_location_pii SET latitude = -91",
        InvariantMutation.LongitudeAboveRange => "UPDATE ie_location_pii SET longitude = 181",
        InvariantMutation.LongitudeBelowRange => "UPDATE ie_location_pii SET longitude = -181",
        InvariantMutation.ReversedAgendaLocalDates => "UPDATE ie_event_agenda_items SET local_start_date = '2026-08-26', local_end_date = '2026-08-25'",
        InvariantMutation.ReversedSessionLocalDates => "UPDATE ie_event_sessions SET local_start_date = '2026-08-26', local_end_date = '2026-08-25'",
        InvariantMutation.ReversedAgendaUtcRange => "UPDATE ie_event_agenda_items SET start_time = '2026-08-25 10:00:00+00:00', end_time = '2026-08-25 09:00:00+00:00'",
        InvariantMutation.ReversedSessionUtcRange => "UPDATE ie_event_sessions SET start_time = '2026-08-25 12:00:00+00:00', end_time = '2026-08-25 11:00:00+00:00'",
        InvariantMutation.InvalidPaymentComposition => "UPDATE ie_payment_attempts SET total_minor = total_minor + 1",
        _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
    };

    public enum InvariantMutation
    {
        NegativeTicketAmount,
        NegativeMinimumTicketAmount,
        NegativeSuggestedTicketAmount,
        PartialCoordinate,
        PartialCoordinateMissingLatitude,
        OutOfRangeCoordinate,
        LatitudeBelowRange,
        LongitudeAboveRange,
        LongitudeBelowRange,
        ReversedAgendaLocalDates,
        ReversedSessionLocalDates,
        ReversedAgendaUtcRange,
        ReversedSessionUtcRange,
        InvalidPaymentComposition
    }
}
