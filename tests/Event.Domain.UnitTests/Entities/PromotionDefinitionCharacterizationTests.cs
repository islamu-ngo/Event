using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class PromotionDefinitionCharacterizationTests
{
    [Test]
    public async Task ExistingRegistrationMoneySnapshots_RemainPinnedAndValidateContributionComposition()
    {
        EventTicketCatalogVersion catalog = CreatePublishedCatalog(1_000);
        RegistrationOrder order = CreateOrder(catalog);
        RegistrationOrderLine line = RegistrationOrderLine.Create(
            catalog,
            catalog.TicketTypes.Single(),
            order.Id,
            2,
            null,
            null);
        PlatformContributionSetting contributionSetting = PlatformContributionSetting.CreateInitial(
            true,
            "Support ISLAMU",
            "Optional contribution",
            [PlatformContributionOption.Create(0, 0, true), PlatformContributionOption.Create(1_000, 1, false)]);

        order.AddLine(line);
        order.SetPlatformContribution(RegistrationOrderPlatformContribution.CreateOrNull(
            order.Id,
            order.TenantId,
            contributionSetting,
            1_000,
            line.LineSubtotalSnapshot,
            "USD"));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 2_000, 50, 1_950, 200));

        await Assert.That(line.LineSubtotalSnapshot).IsEqualTo(2_000);
        await Assert.That(order.OrganizerDirectedTotalMinorSnapshot).IsEqualTo(2_000);
        await Assert.That(order.PlatformFeeTotalMinorSnapshot).IsEqualTo(50);
        await Assert.That(order.OrganizerEarningsTotalMinorSnapshot).IsEqualTo(1_950);
        await Assert.That(order.PlatformContributionTotalMinorSnapshot).IsEqualTo(200);
        await Assert.That(order.TotalDueMinorSnapshot).IsEqualTo(2_200);
        await Assert.That(() => order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 1_999, 50, 1_949, 200)))
            .Throws<ArgumentException>();
    }

    private static RegistrationOrder CreateOrder(EventTicketCatalogVersion catalog) => RegistrationOrder.Create(
        catalog.TenantId,
        catalog.EventId,
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        BookingPartyTypeEnum.Individual,
        catalog.Id,
        RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
        null,
        CapabilityTokenHash.Create(Convert.ToBase64String(new byte[32])),
        catalog.CurrencyCode,
        new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 15, 12, 15, 0, DateTimeKind.Utc));

    private static EventTicketCatalogVersion CreatePublishedCatalog(long fixedPriceMinor)
    {
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "USD", 1);
        EventTicketType ticketType = EventTicketType.Create(
            Guid.CreateVersion7(),
            catalog.TenantId,
            catalog.Id,
            "General admission",
            "USD",
            TicketPricingModeEnum.Fixed,
            Money.Create(fixedPriceMinor, "USD"),
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
        catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
        catalog.Publish();
        return catalog;
    }
}
