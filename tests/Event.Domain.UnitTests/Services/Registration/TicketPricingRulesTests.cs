using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class TicketPricingRulesTests
{
    [Test]
    public async Task TicketingLookupEnums_UseStableIntegerIdentifiers()
    {
        await Assert.That((int)TicketCatalogStatusEnum.Draft).IsEqualTo(1);
        await Assert.That((int)TicketCatalogStatusEnum.Published).IsEqualTo(2);
        await Assert.That((int)TicketCatalogStatusEnum.Retired).IsEqualTo(3);
        await Assert.That((int)TicketPricingModeEnum.Fixed).IsEqualTo(1);
        await Assert.That((int)TicketPricingModeEnum.Free).IsEqualTo(2);
        await Assert.That((int)TicketPricingModeEnum.Donation).IsEqualTo(3);
        await Assert.That((int)TicketPricingModeEnum.PayWhatYouCan).IsEqualTo(4);
        await Assert.That((int)TicketPricingModeEnum.SlidingScale).IsEqualTo(5);
        await Assert.That((int)ParticipantDataCollectionModeEnum.None).IsEqualTo(1);
        await Assert.That((int)ParticipantDataCollectionModeEnum.LeadBookerOnly).IsEqualTo(2);
        await Assert.That((int)ParticipantDataCollectionModeEnum.PerTicketOptional).IsEqualTo(3);
        await Assert.That((int)ParticipantDataCollectionModeEnum.PerTicketRequired).IsEqualTo(4);
        await Assert.That((int)ParticipantDataCollectionModeEnum.DeferredAssignment).IsEqualTo(5);
        await Assert.That((int)EntitlementScopeTypeEnum.Event).IsEqualTo(1);
        await Assert.That((int)EntitlementSelectionRuleEnum.AllIncluded).IsEqualTo(1);
        await Assert.That((int)CapacityOversellPolicyEnum.Disallow).IsEqualTo(1);
    }

    [Test]
    public void ValidateConfiguration_AcceptsEveryPricingModeIncludingZeroMinimum()
    {
        TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Fixed, "EUR", 1_000, null, null);
        TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Free, "JPY", null, null, null);
        TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Donation, "KWD", null, 0, null);
        TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.PayWhatYouCan, "EUR", null, 0, 700);
        TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.SlidingScale, "EUR", null, 500, 1_000);
        TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Free, "XXX", null, null, null);
    }

    [Test]
    public async Task ValidateConfiguration_RejectsInvalidFieldShapesForEveryPricingMode()
    {
        await Assert.That(() => TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Fixed, "USD", null, null, null))
            .Throws<ArgumentException>();
        await Assert.That(() => TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Fixed, "USD", 0, null, null))
            .Throws<ArgumentException>();
        await Assert.That(() => TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Fixed, "USD", -1, null, null))
            .Throws<ArgumentException>();
        await Assert.That(() => TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Free, "USD", 0, null, null))
            .Throws<ArgumentException>();
        await Assert.That(() => TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Donation, "USD", 1, null, null))
            .Throws<ArgumentException>();
        await Assert.That(() => TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.PayWhatYouCan, "USD", 1, null, null))
            .Throws<ArgumentException>();
        await Assert.That(() => TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.SlidingScale, "USD", null, 10, 5))
            .Throws<ArgumentException>();
        await Assert.That(() => TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Donation, "XXX", null, 0, null))
            .Throws<ArgumentException>();
        await Assert.That(() => TicketPricingRules.ValidateConfiguration(TicketPricingModeEnum.Free, "ZZZ", null, null, null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ValidateChosenUnitPriceMinor_UsesExactMinorUnitBounds()
    {
        long donation = TicketPricingRules.ValidateChosenUnitPriceMinor(TicketPricingModeEnum.Donation, "EUR", 0, 0);
        long payWhatYouCan = TicketPricingRules.ValidateChosenUnitPriceMinor(TicketPricingModeEnum.PayWhatYouCan, "JPY", 7, 0);
        long slidingScale = TicketPricingRules.ValidateChosenUnitPriceMinor(TicketPricingModeEnum.SlidingScale, "KWD", 5_000, 5_000);

        await Assert.That(donation).IsEqualTo(0);
        await Assert.That(payWhatYouCan).IsEqualTo(7);
        await Assert.That(slidingScale).IsEqualTo(5_000);
        await Assert.That(() => TicketPricingRules.ValidateChosenUnitPriceMinor(TicketPricingModeEnum.SlidingScale, "EUR", 499, 500))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => TicketPricingRules.ValidateChosenUnitPriceMinor(TicketPricingModeEnum.Donation, "XXX", 0, 0))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CurrencyMetadata_UsesExplicitMinorUnitScalesAndNoCurrencySentinel()
    {
        CurrencyMetadata eur = CurrencyMetadata.Get(" eur ");
        CurrencyMetadata jpy = CurrencyMetadata.Get("JPY");
        CurrencyMetadata kwd = CurrencyMetadata.Get("KWD");
        CurrencyMetadata noCurrency = CurrencyMetadata.Get("XXX");

        await Assert.That(eur.Code).IsEqualTo("EUR");
        await Assert.That(eur.MinorUnitsPerMajorUnit).IsEqualTo(100);
        await Assert.That(jpy.MinorUnitsPerMajorUnit).IsEqualTo(1);
        await Assert.That(kwd.MinorUnitsPerMajorUnit).IsEqualTo(1_000);
        await Assert.That(noCurrency.IsNoCurrency).IsTrue();
        await Assert.That(() => CurrencyMetadata.Get("ZZZ")).Throws<ArgumentException>();
    }

    [Test]
    public async Task MinorUnitMath_AppliesBasisPointsAndRejectsOverflow()
    {
        await Assert.That(MinorUnitMath.ApplyBasisPoints(10_000, 250)).IsEqualTo(250);
        await Assert.That(MinorUnitMath.ApplyBasisPoints(1, 5_000)).IsEqualTo(1);
        await Assert.That(MinorUnitMath.ApplyBasisPoints(3, 5_000)).IsEqualTo(2);
        await Assert.That(MinorUnitMath.ApplyBasisPoints(1, 4_999)).IsEqualTo(0);
        await Assert.That(() => MinorUnitMath.Multiply(long.MaxValue, 2)).Throws<OverflowException>();
        await Assert.That(() => MinorUnitMath.Add(long.MaxValue, 1)).Throws<OverflowException>();
    }
}
