// ABOUTME: Verifies pure in-memory Unicode normalization, case-folding, and boundary invariants for location keys.
// ABOUTME: Shifts algorithmic Unicode verification out of persistence integration tests into sub-millisecond domain unit tests.

using System.Text;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests;

public sealed class UnicodeNormalizationInvariantTests
{
    [Test]
    public async Task CanonicalEquivalenceMatchesComposedAndDecomposedSequences()
    {
        string composedCafe = LocationAddressSubstringKeyV1.Create("café 😀");
        string decomposedCafe = LocationAddressSubstringKeyV1.Create("CAFE\u0301 😀");

        await Assert.That(composedCafe).IsEqualTo(decomposedCafe);
        await Assert.That(composedCafe).IsEqualTo("U000043U000041U000046U0000C9U000020U01F600");

        string composedAngstrom = LocationAddressSubstringKeyV1.Create("Ångström");
        string decomposedAngstrom = LocationAddressSubstringKeyV1.Create("A\u030Angstro\u0308m");
        string lowercaseDecomposed = LocationAddressSubstringKeyV1.Create("a\u030Angstro\u0308m");

        await Assert.That(composedAngstrom).IsEqualTo(decomposedAngstrom);
        await Assert.That(composedAngstrom).IsEqualTo(lowercaseDecomposed);

        // Hangul: Decomposed Jamo vs Precomposed Syllable
        string hangulDecomposed = LocationAddressSubstringKeyV1.Create("\u1100\u1161");
        string hangulPrecomposed = LocationAddressSubstringKeyV1.Create("\uAC00");
        await Assert.That(hangulDecomposed).IsEqualTo(hangulPrecomposed);
    }

    [Test]
    public async Task CaseFoldingProducesDeterministicInvariantKeysAcrossScripts()
    {
        // Greek: medial sigma 'σ', final sigma 'ς', and capital sigma 'Σ' all fold to capital sigma 'Σ' (U+03A3)
        string greekUpper = LocationAddressSubstringKeyV1.Create("Σ");
        string greekMedial = LocationAddressSubstringKeyV1.Create("σ");
        string greekFinal = LocationAddressSubstringKeyV1.Create("ς");
        await Assert.That(greekMedial).IsEqualTo(greekUpper);
        await Assert.That(greekFinal).IsEqualTo(greekUpper);
        await Assert.That(greekUpper).IsEqualTo("U0003A3");

        // French diacritics: lowercase with accent folds to uppercase with accent
        string cafeLower = LocationAddressSubstringKeyV1.Create("café");
        string cafeUpper = LocationAddressSubstringKeyV1.Create("CAFÉ");
        await Assert.That(cafeLower).IsEqualTo(cafeUpper);
        await Assert.That(cafeLower).IsEqualTo("U000043U000041U000046U0000C9");

        // Latin case preservation
        string mixedCase = LocationAddressSubstringKeyV1.Create("rUe De PaRiS");
        string upperCase = LocationAddressSubstringKeyV1.Create("RUE DE PARIS");
        await Assert.That(mixedCase).IsEqualTo(upperCase);
    }

    [Test]
    public async Task LiteralBoundaryAndSqlWildcardsDoNotAlterKeyEncoding()
    {
        // Wildcard characters are treated as pure literal scalar tokens
        string wildcards = LocationAddressSubstringKeyV1.Create("%_");
        await Assert.That(wildcards).IsEqualTo("U000025U00005F");

        string wildcardsWithEmoji = LocationAddressSubstringKeyV1.Create("😀 %");
        await Assert.That(wildcardsWithEmoji).IsEqualTo("U01F600U000020U000025");

        // Verifies wildcard characters never produce SQL control characters
        await Assert.That(wildcards.Contains('%', StringComparison.Ordinal)).IsFalse();
        await Assert.That(wildcards.Contains('_', StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AstralPlaneCharactersMaintainExactScalarBoundaries()
    {
        string emoji = LocationAddressSubstringKeyV1.Create("😀");
        await Assert.That(emoji).IsEqualTo("U01F600");

        string astralScalar = LocationAddressSubstringKeyV1.Create(char.ConvertFromUtf32(0x100004));
        await Assert.That(astralScalar).IsEqualTo("U100004");

        // Adjacent BMP characters cannot accidentally create a false match for an astral scalar
        string adjacentBmp = LocationAddressSubstringKeyV1.Create("AB");
        await Assert.That(adjacentBmp.Contains(astralScalar, StringComparison.Ordinal)).IsFalse();

        // Non-character code points do not corrupt surrounding tokens
        string nonCharFdd0 = char.ConvertFromUtf32(0xFDD0);
        string nonCharPlane = char.ConvertFromUtf32(0x1FFFE);
        string tokensWithNonChars = LocationAddressSubstringKeyV1.Create($"A{nonCharFdd0}B{nonCharPlane}C");
        await Assert.That(tokensWithNonChars).IsEqualTo("U000041U00FDD0U000042U01FFFEU000043");
    }

    [Test]
    public async Task DisplaySortKeyPreservesDeterministicOrdinalOrdering()
    {
        string[] rawItems = ["😀", "\uE000", "AA", "A", "café", "CAFE"];
        string[] sortKeys = rawItems.Select(LocationDisplaySortKeyV1.Create).ToArray();

        Array.Sort(sortKeys, StringComparer.Ordinal);

        string[] expectedSortedKeys =
        [
            LocationDisplaySortKeyV1.Create("A"),
            LocationDisplaySortKeyV1.Create("AA"),
            LocationDisplaySortKeyV1.Create("café"),
            LocationDisplaySortKeyV1.Create("CAFE"),
            LocationDisplaySortKeyV1.Create("\uE000"),
            LocationDisplaySortKeyV1.Create("😀")
        ];
        Array.Sort(expectedSortedKeys, StringComparer.Ordinal);

        await Assert.That(sortKeys).IsEquivalentTo(expectedSortedKeys, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task BoundaryEnforcementRejectsNullEmptyAndOverCapacity()
    {
        ArgumentNullException nullEx = Assert.Throws<ArgumentNullException>(() =>
            LocationAddressSubstringKeyV1.Create(null!));
        ArgumentException emptyEx = Assert.Throws<ArgumentException>(() =>
            LocationAddressSubstringKeyV1.Create(string.Empty));

        await Assert.That(nullEx.ParamName).IsEqualTo("value");
        await Assert.That(emptyEx.ParamName).IsEqualTo("value");

        int maxChars = LocationAddressSubstringKeyV1.MaximumLength / UnicodeScalarKeyV1.TokenWidth;
        string exactlyMax = new('x', maxChars);
        string key = LocationAddressSubstringKeyV1.Create(exactlyMax);
        await Assert.That(key.Length).IsEqualTo(LocationAddressSubstringKeyV1.MaximumLength);

        string overMax = new('x', maxChars + 1);
        Assert.Throws<ArgumentException>(() =>
            LocationAddressSubstringKeyV1.Create(overMax));
    }
}
