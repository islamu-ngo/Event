using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventTicketTypeMoneyBoundaryTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid CatalogId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");

    [Test]
    public async Task CreateFreeXxxPinsNoCurrencyAndNoAmountScalars()
    {
        EventTicketType ticket = Create(TicketPricingModeEnum.Free, "XXX", null, null, null);

        await Assert.That(ticket.CurrencyCode).IsEqualTo("XXX");
        await Assert.That(ticket.FixedPriceMinor).IsNull();
        await Assert.That(ticket.MinimumPriceMinor).IsNull();
        await Assert.That(ticket.SuggestedPriceMinor).IsNull();
    }

    [Test]
    public async Task CreateFixedPinsPositiveNormalizedMoneyAsScalarSnapshot()
    {
        EventTicketType ticket = Create(
            TicketPricingModeEnum.Fixed,
            "EUR",
            Money.Create(1_250, " eur "),
            null,
            null);

        await Assert.That(ticket.CurrencyCode).IsEqualTo("EUR");
        await Assert.That(ticket.FixedPriceMinor).IsEqualTo(1_250);
        await Assert.That(ticket.MinimumPriceMinor).IsNull();
        await Assert.That(ticket.SuggestedPriceMinor).IsNull();
    }

    [Test]
    public async Task CreateDonationAndSlidingScalePreserveNullableOrderedBounds()
    {
        EventTicketType donation = Create(TicketPricingModeEnum.Donation, "USD", null, null, null);
        EventTicketType sliding = Create(
            TicketPricingModeEnum.SlidingScale,
            "USD",
            null,
            Money.Create(500, "USD"),
            Money.Create(1_000, "USD"));

        await Assert.That(donation.MinimumPriceMinor).IsNull();
        await Assert.That(sliding.MinimumPriceMinor).IsEqualTo(500);
        await Assert.That(sliding.SuggestedPriceMinor).IsEqualTo(1_000);
        await Assert.That(() => Create(
                TicketPricingModeEnum.SlidingScale,
                "USD",
                null,
                Money.Create(1_001, "USD"),
                Money.Create(1_000, "USD")))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateRejectsAmountCurrencyThatDisagreesWithTicketCurrency()
    {
        await Assert.That(() => Create(
                TicketPricingModeEnum.Fixed,
                "EUR",
                Money.Create(1_250, "USD"),
                null,
                null))
            .Throws<ArgumentException>();
    }

    private static EventTicketType Create(
        TicketPricingModeEnum pricingMode,
        string currencyCode,
        Money? fixedPrice,
        Money? minimumPrice,
        Money? suggestedPrice) => EventTicketType.Create(
        Guid.CreateVersion7(),
        TenantId,
        CatalogId,
        "General admission",
        currencyCode,
        pricingMode,
        fixedPrice,
        minimumPrice,
        suggestedPrice,
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
}
