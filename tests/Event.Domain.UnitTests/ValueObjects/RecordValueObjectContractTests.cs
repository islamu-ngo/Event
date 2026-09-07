using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class RecordValueObjectContractTests
{
    [Test]
    public async Task CapabilityTokenHashEqualValuesAndRecordCopiesPreserveTheValidatedHash()
    {
        string value = CanonicalHash(17);
        CapabilityTokenHash original = CapabilityTokenHash.Create(value);
        CapabilityTokenHash equal = CapabilityTokenHash.Create(value);
        CapabilityTokenHash copy = original with { };

        await Assert.That(original).IsEqualTo(equal);
        await Assert.That(copy).IsEqualTo(original);
        await Assert.That(ReferenceEquals(copy, original)).IsFalse();
        await Assert.That(copy.Value).IsEqualTo(value);
        await Assert.That(copy.ToString()).DoesNotContain(value);
    }

    [Test]
    public async Task CapabilityTokenHashDifferentValidatedValuesAreNotEqual()
    {
        CapabilityTokenHash first = CapabilityTokenHash.Create(CanonicalHash(17));
        CapabilityTokenHash second = CapabilityTokenHash.Create(CanonicalHash(18));

        await Assert.That(first).IsNotEqualTo(second);
    }

    [Test]
    public async Task ExternalActionUrlNormalizedFactsDetermineEqualityAndRecordCopyBehavior()
    {
        ExternalActionUrl original = ExternalActionUrl.Create(" https://EVENTS.example.test:443/register?source=islamu ");
        ExternalActionUrl equal = ExternalActionUrl.Create("https://events.example.test/register?source=islamu");
        ExternalActionUrl different = ExternalActionUrl.Create("https://events.example.test/register?source=partner");
        ExternalActionUrl copy = original with { };

        await Assert.That(original).IsEqualTo(equal);
        await Assert.That(original).IsNotEqualTo(different);
        await Assert.That(copy).IsEqualTo(original);
        await Assert.That(ReferenceEquals(copy, original)).IsFalse();
        await Assert.That(copy.DestinationDomain).IsEqualTo("events.example.test");
    }

    [Test]
    public async Task CurrencyMetadataFactoryValuesSupportValueCopiesAndOneFactWithVariants()
    {
        CurrencyMetadata original = CurrencyMetadata.Get(" eur ");
        CurrencyMetadata equal = CurrencyMetadata.Get("EUR");
        CurrencyMetadata copy = original;
        CurrencyMetadata variant = copy with { Code = "USD" };

        await Assert.That(original).IsEqualTo(equal);
        await Assert.That(copy).IsEqualTo(original);
        await Assert.That(variant).IsNotEqualTo(original);
        await Assert.That(variant.Code).IsEqualTo("USD");
        await Assert.That(variant.MinorUnitDigits).IsEqualTo(original.MinorUnitDigits);
        await Assert.That(original.Code).IsEqualTo("EUR");
    }

    [Test]
    public async Task CurrencyMetadataGetRejectsInvalidCodes()
    {
        string?[] invalidCodes = [null, string.Empty, " ", "EU", "EURO", "E1R", "ZZZ"];

        foreach (string? invalidCode in invalidCodes)
        {
            await Assert.That(() => CurrencyMetadata.Get(invalidCode!)).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task VerifiedPurchaserIdentityFactoriesNormalizeValuesAndUseValueEquality()
    {
        Guid accountUserId = Guid.Parse("0198d778-a15b-7f81-bc86-70b92aa6d104");
        VerifiedPurchaserIdentity account = VerifiedPurchaserIdentity.Account(accountUserId);
        VerifiedPurchaserIdentity equalAccount = VerifiedPurchaserIdentity.Account(accountUserId);
        VerifiedPurchaserIdentity email = VerifiedPurchaserIdentity.Email(" buyer@example.test ");

        await Assert.That(account).IsEqualTo(equalAccount);
        await Assert.That(account.Kind).IsEqualTo(nameof(VerifiedPurchaserIdentity.Account));
        await Assert.That(account.Value).IsEqualTo(accountUserId.ToString("D"));
        await Assert.That(email.Kind).IsEqualTo(nameof(VerifiedPurchaserIdentity.Email));
        await Assert.That(email.Value).IsEqualTo("BUYER@EXAMPLE.TEST");
        await Assert.That(email).IsNotEqualTo(account);
    }

    [Test]
    public async Task VerifiedPurchaserIdentityOneFactWithVariantDoesNotMutateTheOriginal()
    {
        VerifiedPurchaserIdentity original = VerifiedPurchaserIdentity.Email("buyer@example.test");
        VerifiedPurchaserIdentity variant = original with { Value = "OTHER@EXAMPLE.TEST" };

        await Assert.That(variant).IsNotEqualTo(original);
        await Assert.That(ReferenceEquals(variant, original)).IsFalse();
        await Assert.That(variant.Kind).IsEqualTo(original.Kind);
        await Assert.That(variant.Value).IsEqualTo("OTHER@EXAMPLE.TEST");
        await Assert.That(original.Value).IsEqualTo("BUYER@EXAMPLE.TEST");
    }

    [Test]
    public async Task VerifiedPurchaserIdentityFactoriesRejectMissingIdentityValues()
    {
        await Assert.That(() => VerifiedPurchaserIdentity.Account(Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => VerifiedPurchaserIdentity.Actor(Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => VerifiedPurchaserIdentity.Email(string.Empty)).Throws<ArgumentException>();
        await Assert.That(() => VerifiedPurchaserIdentity.Email(" ")).Throws<ArgumentException>();
    }

    private static string CanonicalHash(byte fill) =>
        Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray());
}
