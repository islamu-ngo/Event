using Explore.Domain.Common.Localization;

namespace Event.Application.UnitTests.Domain.Common.Localization;

public class CultureRegistryTests
{
    [Test]
    public async Task GetAll_ReturnsEnFrAr_InOrder()
    {
        var all = CultureRegistry.GetAll();

        await Assert.That(all.Count).IsEqualTo(3);
        await Assert.That(all[0].Code).IsEqualTo("en");
        await Assert.That(all[1].Code).IsEqualTo("fr");
        await Assert.That(all[2].Code).IsEqualTo("ar");
    }

    [Test]
    [Arguments("en")]
    [Arguments("FR")]
    [Arguments(" ar ")]
    [Arguments("Ar")]
    public async Task TryGetEntry_WhenCodeIsKnown_ReturnsTrue(string code)
    {
        var found = CultureRegistry.TryGetEntry(code, out var entry);

        await Assert.That(found).IsTrue();
        await Assert.That(entry).IsNotNull();
    }

    [Test]
    [Arguments("")]
    [Arguments(null)]
    [Arguments("en-US")]
    [Arguments("zzz")]
    [Arguments("<script>")]
    [Arguments("fr_FR")]
    public async Task TryGetEntry_WhenCodeIsInvalid_ReturnsFalse(string? code)
    {
        var found = CultureRegistry.TryGetEntry(code, out _);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task ArabicEntry_IsMarkedRtl()
    {
        CultureRegistry.TryGetEntry("ar", out var entry);

        await Assert.That(entry.IsRtl).IsTrue();
        await Assert.That(RtlLanguages.IsRtl("ar")).IsTrue();
    }

    [Test]
    [Arguments("en")]
    [Arguments("fr")]
    [Arguments("unknown")]
    [Arguments(null)]
    public async Task RtlLanguages_NonRtlOrUnknown_ReturnsFalse(string? code)
    {
        await Assert.That(RtlLanguages.IsRtl(code)).IsFalse();
    }

    [Test]
    public async Task Normalize_TrimsAndLowercases_ButRejectsNonIso639_1()
    {
        await Assert.That(CultureRegistry.Normalize(" EN ")).IsEqualTo("en");
        await Assert.That(CultureRegistry.Normalize("en-us")).IsEqualTo(string.Empty);
        await Assert.That(CultureRegistry.Normalize("123")).IsEqualTo(string.Empty);
        await Assert.That(CultureRegistry.Normalize(null)).IsEqualTo(string.Empty);
    }
}
