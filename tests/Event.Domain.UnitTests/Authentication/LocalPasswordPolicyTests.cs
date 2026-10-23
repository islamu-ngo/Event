using Explore.Domain.Authentication;

namespace Event.Domain.UnitTests.Authentication;

public sealed class LocalPasswordPolicyTests
{
    [Test]
    [Arguments(14, false)]
    [Arguments(15, true)]
    [Arguments(128, true)]
    [Arguments(129, false)]
    public async Task AsciiBoundariesMatchScalarBoundaries(int count, bool accepted)
    {
        string password = new('x', count);
        await Assert.That(LocalPasswordPolicy.TryNormalizeNewCredential(password, out _)).IsEqualTo(accepted);
    }

    [Test]
    [Arguments(14, false)]
    [Arguments(15, true)]
    [Arguments(128, true)]
    [Arguments(129, false)]
    public async Task CreationCountsUnicodeScalarsInsteadOfUtf16Units(int count, bool accepted)
    {
        string scalar = char.ConvertFromUtf32(0x1F642);
        string password = string.Concat(Enumerable.Repeat(scalar, count));
        await Assert.That(LocalPasswordPolicy.TryNormalizeNewCredential(password, out _)).IsEqualTo(accepted);
    }

    [Test]
    public async Task CanonicalEquivalentsShareIdentityWithoutTrimmingOrCaseFolding()
    {
        string prefix = new('A', 32);
        string composed = prefix + char.ConvertFromUtf32(0xE9);
        string decomposed = prefix + char.ConvertFromUtf32(0x65) + char.ConvertFromUtf32(0x301);
        await Assert.That(LocalPasswordPolicy.TryNormalizeNewCredential(composed, out string first)).IsTrue();
        await Assert.That(LocalPasswordPolicy.TryNormalizeNewCredential(decomposed, out string second)).IsTrue();
        await Assert.That(first == second).IsTrue();
        LocalPasswordPolicy.TryNormalizeForVerification(" " + composed + " ", out string spaced);
        await Assert.That(first == spaced).IsFalse();
        LocalPasswordPolicy.TryNormalizeForVerification(composed.ToLowerInvariant(), out string lower);
        await Assert.That(first == lower).IsFalse();
    }

    [Test]
    public async Task LoginShapeDoesNotApplyCreationMinimumOrComposition()
    {
        string shortPassword = new('x', 14);
        await Assert.That(LocalPasswordPolicy.TryNormalizeForVerification(shortPassword, out _)).IsTrue();
        await Assert.That(LocalPasswordPolicy.TryNormalizeNewCredential(shortPassword, out _)).IsFalse();
        string phrase = string.Join(' ', Enumerable.Range(0, 5).Select(index => $"segment{index}"));
        await Assert.That(LocalPasswordPolicy.TryNormalizeNewCredential(phrase, out _)).IsTrue();
        await Assert.That(LocalPasswordPolicy.TryNormalizeNewCredential(new string(' ', 15), out _)).IsTrue();
    }

    [Test]
    public async Task MalformedUtf16AndEmptyInputAreRejectedWithoutReplacement()
    {
        await Assert.That(LocalPasswordPolicy.TryNormalizeForVerification(null, out _)).IsFalse();
        await Assert.That(LocalPasswordPolicy.TryNormalizeForVerification(string.Empty, out _)).IsFalse();
        string malformed = new((char)0xD800, 15);
        await Assert.That(LocalPasswordPolicy.TryNormalizeNewCredential(malformed, out _)).IsFalse();
    }
}
