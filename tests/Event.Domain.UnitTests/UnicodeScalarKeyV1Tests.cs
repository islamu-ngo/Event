// ABOUTME: Freezes the version-one Unicode scalar key algorithm and its exact ordinal representation.
// ABOUTME: Covers canonical equivalence, casing, astral scalars, strict decoding, bounds, and token alignment.

using System.Security.Cryptography;
using System.Text;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests;

public sealed class UnicodeScalarKeyV1Tests
{
    [Test]
    public async Task ExactTokensPreserveCanonicalCaseAndAstralSemantics()
    {
        string composed = LocationAddressSubstringKeyV1.Create("é");
        string decomposed = LocationAddressSubstringKeyV1.Create("e\u0301");

        await Assert.That(composed).IsEqualTo("U0000C9");
        await Assert.That(decomposed).IsEqualTo("U0000C9");
        await Assert.That(LocationAddressSubstringKeyV1.Create("rUe"))
            .IsEqualTo("U000052U000055U000045");
        await Assert.That(LocationAddressSubstringKeyV1.Create("😀"))
            .IsEqualTo("U01F600");
        await Assert.That(LocationAddressSubstringKeyV1.Version).IsEqualTo((short)1);
        await Assert.That(LocationDisplaySortKeyV1.Version).IsEqualTo((short)1);
    }

    [Test]
    public async Task NullEmptyAndMalformedUtf16FailWithTypedValueParameter()
    {
        ArgumentNullException nullFailure = Assert.Throws<ArgumentNullException>(() =>
            UnicodeScalarKeyV1.Encode(null!, UnicodeScalarKeyV1.TokenWidth));
        ArgumentException emptyFailure = Assert.Throws<ArgumentException>(() =>
            UnicodeScalarKeyV1.Encode(string.Empty, UnicodeScalarKeyV1.TokenWidth));
        ArgumentException malformedStart = Assert.Throws<ArgumentException>(() =>
            UnicodeScalarKeyV1.Encode("\uD800A", 2 * UnicodeScalarKeyV1.TokenWidth));
        ArgumentException malformedMiddle = Assert.Throws<ArgumentException>(() =>
            UnicodeScalarKeyV1.Encode("A\uD800B", 3 * UnicodeScalarKeyV1.TokenWidth));

        await Assert.That(nullFailure.ParamName).IsEqualTo("value");
        await Assert.That(emptyFailure.ParamName).IsEqualTo("value");
        await Assert.That(malformedStart.ParamName).IsEqualTo("value");
        await Assert.That(malformedMiddle.ParamName).IsEqualTo("value");
    }

    [Test]
    public async Task TokenLengthBoundariesAreExactAndWrapperContractsAreIndependent()
    {
        string maximumAddress = new('a', LocationAddressSubstringKeyV1.MaximumLength / UnicodeScalarKeyV1.TokenWidth);

        await Assert.That(UnicodeScalarKeyV1.Encode("A", UnicodeScalarKeyV1.TokenWidth))
            .IsEqualTo("U000041");
        await Assert.That(LocationAddressSubstringKeyV1.Create(maximumAddress).Length)
            .IsEqualTo(LocationAddressSubstringKeyV1.MaximumLength);
        await Assert.That(LocationAddressSubstringKeyV1.MaximumLength).IsEqualTo(14_000);
        await Assert.That(LocationDisplaySortKeyV1.MaximumLength).IsEqualTo(14_000);
        await Assert.That(LocationAddressSubstringKeyV1.Version).IsEqualTo((short)1);
        await Assert.That(LocationDisplaySortKeyV1.Version).IsEqualTo((short)1);

        ArgumentException oneTokenOverflow = Assert.Throws<ArgumentException>(() =>
            UnicodeScalarKeyV1.Encode("AB", UnicodeScalarKeyV1.TokenWidth));
        ArgumentException smallerThanToken = Assert.Throws<ArgumentException>(() =>
            UnicodeScalarKeyV1.Encode("A", UnicodeScalarKeyV1.TokenWidth - 1));
        ArgumentException wrapperOverflow = Assert.Throws<ArgumentException>(() =>
            LocationAddressSubstringKeyV1.Create(maximumAddress + "a"));
        await Assert.That(oneTokenOverflow.ParamName).IsEqualTo("value");
        await Assert.That(smallerThanToken.ParamName).IsEqualTo("value");
        await Assert.That(wrapperOverflow.ParamName).IsEqualTo("value");
        await Assert.That(() => UnicodeScalarKeyV1.Encode("A", -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task NonCharacterBoundariesFlushSegmentsWithoutDuplicatingAdjacentScalars()
    {
        string fdcf = char.ConvertFromUtf32(0xFDCF);
        string fdd0 = char.ConvertFromUtf32(0xFDD0);
        string fdef = char.ConvertFromUtf32(0xFDEF);
        string fdf0 = char.ConvertFromUtf32(0xFDF0);
        string planeFffe = char.ConvertFromUtf32(0x1FFFE);
        string planeFfff = char.ConvertFromUtf32(0x1FFFF);

        await Assert.That(LocationAddressSubstringKeyV1.Create($"a{fdd0}b"))
            .IsEqualTo("U000041U00FDD0U000042");
        await Assert.That(LocationAddressSubstringKeyV1.Create($"a{fdef}b"))
            .IsEqualTo("U000041U00FDEFU000042");
        await Assert.That(LocationAddressSubstringKeyV1.Create($"a{planeFffe}b"))
            .IsEqualTo("U000041U01FFFEU000042");
        await Assert.That(LocationAddressSubstringKeyV1.Create($"a{planeFfff}b"))
            .IsEqualTo("U000041U01FFFFU000042");
        await Assert.That(LocationAddressSubstringKeyV1.Create($"e\u0301{fdcf}e\u0301"))
            .IsEqualTo("U0000C9U00FDCFU0000C9");
        await Assert.That(LocationAddressSubstringKeyV1.Create($"e\u0301{fdf0}e\u0301"))
            .IsEqualTo("U0000C9U00FDF0U0000C9");
        await Assert.That(LocationAddressSubstringKeyV1.Create($"e\u0301{fdd0}e\u0301"))
            .IsEqualTo(LocationAddressSubstringKeyV1.Create($"é{fdd0}é"));
    }

    [Test]
    public async Task SentinelPreventsCrossScalarFalseSubstringMatches()
    {
        string adjacent = LocationAddressSubstringKeyV1.Create("AB");
        string crossBoundaryScalar = LocationAddressSubstringKeyV1.Create(char.ConvertFromUtf32(0x100004));

        await Assert.That(adjacent).IsEqualTo("U000041U000042");
        await Assert.That(crossBoundaryScalar).IsEqualTo("U100004");
        await Assert.That(adjacent.Contains(crossBoundaryScalar, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task OrdinalComparisonPreservesPrefixBmpAndAstralOrder()
    {
        string[] keys = [
            LocationDisplaySortKeyV1.Create("😀"),
            LocationDisplaySortKeyV1.Create("\uE000"),
            LocationDisplaySortKeyV1.Create("AA"),
            LocationDisplaySortKeyV1.Create("A")
        ];

        Array.Sort(keys, StringComparer.Ordinal);

        await Assert.That(keys).IsEquivalentTo(
            ["U000041", "U000041U000041", "U00E000", "U01F600"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task UnicodeScalarCorpusMatchesVersionOneGoldenDigest()
    {
        var corpus = new StringBuilder(8_000_000);
        for (int value = 0; value <= 0x10FFFF; value++)
        {
            if (value is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            corpus.Append(LocationAddressSubstringKeyV1.Create(char.ConvertFromUtf32(value))).Append('\n');
        }

        foreach (string sequence in new[]
        {
            "e\u0301", "é", "a\u0308\u0301", "A\u0308\u0301", "Σσς", "iIıİ", "\U00010428\U00010400"
        })
        {
            corpus.Append(LocationAddressSubstringKeyV1.Create(sequence)).Append('\n');
        }

        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(corpus.ToString())));
        string[] knownGoldenDigests =
        [
            "96DE6714789B6B199E8EF0F70D94595980DD057E404D1452C94BDE0C639FD0E6", // Unicode 16 / ICU 78+
            "789689B89558B27E815FB63765F74118D97C2D1ABF1A130DF2D2DEE9EB9CD16D", // Unicode 15.1 / ICU 74 (Ubuntu runner)
        ];
        await Assert.That(knownGoldenDigests).Contains(digest);
    }
}
