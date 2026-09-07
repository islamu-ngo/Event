using Explore.Blazor.Client.Localization;

namespace Explore.Blazor.Client.Tests.Localization;

public class CultureRegistryTests
{
    [Test]
    public async Task GetAll_ReturnsSupportedCulturesInStableOrder()
    {
        var cultures = CultureRegistry.GetAll();

        await Assert.That(cultures.Count).IsEqualTo(3);
        await Assert.That(cultures[0].Code).IsEqualTo("en");
        await Assert.That(cultures[1].Code).IsEqualTo("fr");
        await Assert.That(cultures[2].Code).IsEqualTo("ar");
        await Assert.That(cultures.Single(culture => culture.Code == "ar").IsRtl).IsTrue();
    }

    [Test]
    [Arguments(" FR ", "fr")]
    [Arguments("ar", "ar")]
    public async Task TryGetEntry_ValidCode_NormalizesCode(string input, string expected)
    {
        var found = CultureRegistry.TryGetEntry(input, out var culture);

        await Assert.That(found).IsTrue();
        await Assert.That(culture.Code).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("english")]
    [Arguments("e1")]
    [Arguments("de")]
    public async Task Contains_UnsupportedCode_ReturnsFalse(string? input)
    {
        await Assert.That(CultureRegistry.Contains(input)).IsFalse();
    }
}
