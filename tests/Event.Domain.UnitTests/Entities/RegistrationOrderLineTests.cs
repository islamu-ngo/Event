using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationOrderLineTests
{
    [Test]
    public async Task Create_UsesPinnedTicketPricingAndLeavesExistingLineUnchangedWhenCatalogChanges()
    {
        EventTicketCatalogVersion catalog = CreatePublishedCatalog(TicketPricingModeEnum.Fixed, 1_000, null);
        EventTicketType ticketType = catalog.TicketTypes.Single();
        RegistrationOrderLine line = RegistrationOrderLine.Create(catalog, ticketType, Guid.CreateVersion7(), 2, null, null);
        EventTicketCatalogVersion nextCatalog = catalog.CloneToDraft();
        EventTicketType nextTicketType = nextCatalog.TicketTypes.Single();

        nextCatalog.UpdateTicketPricing(nextTicketType, TicketPricingModeEnum.Fixed, Money.Create(2_000, "USD"), null, null);

        await Assert.That(line.TicketCatalogVersionId).IsEqualTo(catalog.Id);
        await Assert.That(line.TicketTypeNameSnapshot).IsEqualTo("General admission");
        await Assert.That(line.UnitPriceAmountSnapshot).IsEqualTo(1_000);
        await Assert.That(line.ChosenUnitPriceAmountSnapshot).IsNull();
        await Assert.That(line.LineSubtotalSnapshot).IsEqualTo(2_000);
        await Assert.That(line.PlatformFeePolicyVersionSnapshot).IsNull();
    }

    [Test]
    public async Task Create_ValidatesBuyerAmountAgainstPinnedMinimumAndAllowsZeroWhenMinimumIsZero()
    {
        EventTicketCatalogVersion minimumCatalog = CreatePublishedCatalog(TicketPricingModeEnum.Donation, null, 500);
        EventTicketType minimumTicket = minimumCatalog.TicketTypes.Single();
        EventTicketCatalogVersion zeroCatalog = CreatePublishedCatalog(TicketPricingModeEnum.PayWhatYouCan, null, 0);
        EventTicketType zeroTicket = zeroCatalog.TicketTypes.Single();

        await Assert.That(() => RegistrationOrderLine.Create(
                minimumCatalog,
                minimumTicket,
                Guid.CreateVersion7(),
                1,
                499,
                null))
            .Throws<ArgumentOutOfRangeException>();

        RegistrationOrderLine zeroLine = RegistrationOrderLine.Create(
            zeroCatalog,
            zeroTicket,
            Guid.CreateVersion7(),
            3,
            0,
            null);

        await Assert.That(zeroLine.ChosenUnitPriceAmountSnapshot).IsEqualTo(0);
        await Assert.That(zeroLine.UnitPriceAmountSnapshot).IsEqualTo(0);
        await Assert.That(zeroLine.LineSubtotalSnapshot).IsEqualTo(0);
        await Assert.That(zeroLine.MinimumPriceAmountSnapshot).IsEqualTo(0);
    }

    [Test]
    public async Task Create_WhenFeePolicyProducesFee_PinsItsVersion()
    {
        EventTicketCatalogVersion catalog = CreatePublishedCatalog(TicketPricingModeEnum.Fixed, 1_000, null);
        PlatformFeePolicy feePolicy = PlatformFeePolicy.CreateDefault().CreateRevision(
            true,
            250,
            [PlatformFeeFixedCharge.Create("USD", 25)]);

        RegistrationOrderLine line = RegistrationOrderLine.Create(
            catalog,
            catalog.TicketTypes.Single(),
            Guid.CreateVersion7(),
            1,
            null,
            feePolicy);

        await Assert.That(line.PlatformFeePolicyVersionSnapshot).IsEqualTo(2);
    }

    private static EventTicketCatalogVersion CreatePublishedCatalog(
        TicketPricingModeEnum pricingMode,
        long? fixedPriceMinor,
        long? minimumPriceMinor)
    {
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "USD", 1);
        EventTicketType ticketType = EventTicketType.Create(
            Guid.CreateVersion7(),
            catalog.TenantId,
            catalog.Id,
            "General admission",
            "USD",
            pricingMode,
            CreateMoney(fixedPriceMinor),
            CreateMoney(minimumPriceMinor),
            pricingMode == TicketPricingModeEnum.PayWhatYouCan ? Money.Create(1_000, "USD") : null,
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
        catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
        catalog.Publish();
        return catalog;
    }

    private static Money? CreateMoney(long? minorUnits) =>
        minorUnits.HasValue ? Money.Create(minorUnits.Value, "USD") : null;
}
