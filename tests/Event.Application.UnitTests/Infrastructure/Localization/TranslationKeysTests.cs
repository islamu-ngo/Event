using Explore.Application.Localization;

namespace Event.Application.UnitTests.Infrastructure.Localization;

public class TranslationKeysTests
{
    [Test]
    public async Task Lookup_BuildsKeyFromEntityTypeMasterCodeAndField()
    {
        string key = TranslationKeys.Lookup("tag", "FIQH", "full_name");

        await Assert.That(key).IsEqualTo("lookup.tag.FIQH.full_name");
    }

    [Test]
    public async Task Lookup_NormalizesEntityTypeAndFieldButPreservesMasterCode()
    {
        string key = TranslationKeys.Lookup(" Tag ", "SISTANI_MARJA", " Full_Name ");

        await Assert.That(key).IsEqualTo("lookup.tag.SISTANI_MARJA.full_name");
    }

    [Test]
    [Arguments("tag", "FIQH", "")]
    [Arguments("tag", "", "full_name")]
    [Arguments("", "FIQH", "full_name")]
    [Arguments("tag.name", "FIQH", "full_name")]
    [Arguments("tag", "Islamic Jurisprudence", "full_name")]
    [Arguments("tag", "FIQH", "full.name")]
    public async Task Lookup_WhenSegmentIsInvalid_ThrowsArgumentException(
        string entityType,
        string masterCode,
        string field)
    {
        await Assert.That(() => TranslationKeys.Lookup(entityType, masterCode, field))
            .Throws<ArgumentException>();
    }
}
