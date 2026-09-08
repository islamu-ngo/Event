using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

public sealed class LocationTextNormalizationTests
{
    [Test]
    [Arguments("Cafe\u0301", "CAFÉ")]
    [Arguments("a\u030Angstro\u0308m", "ÅNGSTRÖM")]
    [Arguments("\u1100\u1161", "\uAC00")]
    [Arguments("é", "É")]
    [Arguments("Straße", "STRAßE")]
    [Arguments("İstanbul", "İSTANBUL")]
    [Arguments("ςσ", "ΣΣ")]
    [Arguments("\U00010428", "\U00010400")]
    [Arguments("مَسْجِد\u200C\u200D", "مَسْجِد\u200C\u200D")]
    [Arguments("👩🏽\u200D💻\uFE0F", "👩🏽\u200D💻\uFE0F")]
    [Arguments("\uE000\U000F0000", "\uE000\U000F0000")]
    [Arguments(" %_\\[] ", " %_\\[] ")]
    public async Task WritesPreserveOriginalAndProduceCompleteNormalizedText(string source, string expected)
    {
        Location location = NewLocation();
        location.FullName = source;
        location.SetManualAddress(source, "1000");

        await Assert.That(location.FullName).IsEqualTo(source);
        await Assert.That(location.Address).IsEqualTo(source);
        await Assert.That(location.DisplaySortKey).IsEqualTo(expected);
        await Assert.That(location.Pii!.AddressSubstringKey).IsEqualTo(expected);
    }

    [Test]
    public async Task UppercaseMatchingDoesNotRemoveAccentsOrPerformFullCaseFolding()
    {
        Location location = NewLocation();
        location.SetManualAddress("é Straße İstanbul", "1000");
        string key = location.Pii!.AddressSubstringKey;
        await Assert.That(key.Contains('E')).IsTrue();
        await Assert.That(key.StartsWith('E')).IsFalse();
        await Assert.That(key.Contains("STRASSE", StringComparison.Ordinal)).IsFalse();
        await Assert.That(key.Contains("ISTANBUL", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task SourceBoundaryAndCanonicalExpansionRetainTheEntireSearchableSuffix()
    {
        Location location = NewLocation();
        string source = new string('a', 493) + " suffix";
        location.SetManualAddress(source, "1000");
        await Assert.That(location.Pii!.AddressSubstringKey).IsEqualTo(new string('A', 493) + " SUFFIX");

        location.SetManualAddress(new string('\u0344', 500), "1000");
        await Assert.That(location.Pii.AddressSubstringKey)
            .IsEqualTo(string.Concat(Enumerable.Repeat("\u0308\u0301", 500)));
        await Assert.That(location.Pii.AddressSubstringKey.Length).IsEqualTo(1000);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" \t")]
    [Arguments("a\0b")]
    [Arguments("a\uD800b")]
    [Arguments("a\uDC00b")]
    [Arguments("a\uFDD0b")]
    [Arguments("a\uFDEFb")]
    [Arguments("a\uFFFEb")]
    [Arguments("a\U0001FFFFb")]
    [Arguments("a\U0010FFFEb")]
    public async Task RejectedInputCannotPartiallyChangeNameAddressKeysOrStamp(string? invalid)
    {
        Location location = NewLocation();
        location.SetManualAddress("Original address", "1000");
        string nameKey = location.DisplaySortKey;
        string addressKey = location.Pii!.AddressSubstringKey;
        Guid stamp = location.ConcurrencyStamp;

        await Assert.That(() => location.FullName = invalid!).Throws<ArgumentException>();
        await Assert.That(() => location.SetManualAddress(invalid!, "2000")).Throws<ArgumentException>();

        await Assert.That(location.FullName).IsEqualTo("Original venue");
        await Assert.That(location.Address).IsEqualTo("Original address");
        await Assert.That(location.Postcode).IsEqualTo("1000");
        await Assert.That(location.DisplaySortKey).IsEqualTo(nameKey);
        await Assert.That(location.Pii.AddressSubstringKey).IsEqualTo(addressKey);
        await Assert.That(location.ConcurrencyStamp).IsEqualTo(stamp);
    }

    [Test]
    public async Task RawOverflowRejectsBeforeAggregateMutation()
    {
        Location location = NewLocation();
        location.SetManualAddress("Original address", "1000");
        string overflow = new('a', 501);
        await Assert.That(() => location.FullName = overflow).Throws<ArgumentException>();
        await Assert.That(() => location.SetManualAddress(overflow, "2000")).Throws<ArgumentException>();
        await Assert.That(location.FullName).IsEqualTo("Original venue");
        await Assert.That(location.Address).IsEqualTo("Original address");
    }

    [Test]
    public async Task RepeatedErasureAndSubsequentWriteCannotRecreateDerivedAddressPii()
    {
        Location location = NewLocation();
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());
        location.SetManualAddress("Sensitive café address", "1000");
        DateTime erasedAt = new(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc);
        location.EraseOwnedPii(erasedAt, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
        location.EraseOwnedPii(erasedAt, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);

        await Assert.That(() => location.SetManualAddress("Sensitive café address", "1000"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => location.PromoteAddressToTenantApproved(Guid.CreateVersion7(), erasedAt))
            .Throws<InvalidOperationException>();
        await Assert.That(location.Pii).IsNull();
        await Assert.That(location.DisplaySortKey).IsEqualTo("PRIVATE VENUE");
    }

    private static Location NewLocation() => new()
    {
        Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(),
        FullName = "Original venue", Country = "BE", City = "Brussels",
        ConcurrencyStamp = Guid.CreateVersion7()
    };
}
