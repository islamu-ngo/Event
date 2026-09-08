using Explore.Domain;
using Explore.Domain.Enums;
using TUnit.Core;

namespace Event.Domain.UnitTests;

[Category("EventLocationPrivacyLookup")]
public sealed class LocationPrivacyLookupContractTests
{
    [Test]
    public async Task LookupEnumsUseStableIntegerIdentifiers()
    {
        await Assert.That((int)LocationKindEnum.Unclassified).IsEqualTo(1);
        await Assert.That((int)LocationKindEnum.CommercialVenue).IsEqualTo(2);
        await Assert.That((int)LocationKindEnum.PublicSpace).IsEqualTo(3);
        await Assert.That((int)LocationKindEnum.CommunityVenue).IsEqualTo(4);
        await Assert.That((int)LocationKindEnum.PrivateHome).IsEqualTo(5);

        await Assert.That((int)LocationPrivacyStateEnum.NotProvided).IsEqualTo(1);
        await Assert.That((int)LocationPrivacyStateEnum.Active).IsEqualTo(2);
        await Assert.That((int)LocationPrivacyStateEnum.Erased).IsEqualTo(3);

        await Assert.That((int)LocationDisclosureAudienceEnum.Never).IsEqualTo(1);
        await Assert.That((int)LocationDisclosureAudienceEnum.AnyCurrentRegistrant).IsEqualTo(2);
        await Assert.That((int)LocationDisclosureAudienceEnum.ConfirmedParticipant).IsEqualTo(3);
    }

    [Test]
    public async Task LocationKindHasOnlyCanonicalLookupFields()
    {
        var propertyNames = typeof(LocationKind)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(propertyNames.SequenceEqual(
            ["Description", "FullName", "Id", "MasterCode"])).IsTrue();
        await Assert.That(Enum.GetNames<LocationKindEnum>().Contains("Virtual", StringComparer.OrdinalIgnoreCase)).IsFalse();
    }
}
