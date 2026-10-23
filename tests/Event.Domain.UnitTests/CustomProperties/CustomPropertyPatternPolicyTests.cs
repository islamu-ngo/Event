using Explore.Domain.CustomProperties;

namespace Event.Domain.UnitTests.CustomProperties;

public sealed class CustomPropertyPatternPolicyTests
{
    [Test]
    [Arguments(null, true)]
    [Arguments("", true)]
    [Arguments("^[a-z]+$", true)]
    [Arguments("(a+)+$", true)]
    [Arguments("[", false)]
    [Arguments(@"(a)\1", false)]
    [Arguments("(?=a)a", false)]
    [Arguments("(?<=a)b", false)]
    public async Task DefinitionsAdmitOnlyBoundedNonBacktrackingSyntax(string? pattern, bool accepted)
    {
        await Assert.That(CustomPropertyPatternPolicy.IsValid(pattern)).IsEqualTo(accepted);
    }

    [Test]
    public async Task ExactPatternLimitIsAcceptedAndNextCharacterIsRejected()
    {
        await Assert.That(CustomPropertyPatternPolicy.IsValid("(?#" + new string('a', 2044) + ")")).IsTrue();
        await Assert.That(CustomPropertyPatternPolicy.IsValid("(?#" + new string('a', 2045) + ")")).IsFalse();
    }

    [Test]
    public async Task ValuesRespectBoundsAndPatternWithoutThrowingOnInvalidDefinitions()
    {
        await Assert.That(CustomPropertyPatternPolicy.Matches("abc", "^[a-z]+$", 3, 3)).IsTrue();
        await Assert.That(CustomPropertyPatternPolicy.Matches("abcd", "^[a-z]+$", 3, 3)).IsFalse();
        await Assert.That(CustomPropertyPatternPolicy.Matches("ab", "^[a-z]+$", 3, 3)).IsFalse();
        await Assert.That(CustomPropertyPatternPolicy.Matches("123", "^[a-z]+$", null, 3)).IsFalse();
        await Assert.That(CustomPropertyPatternPolicy.Matches("abc", "[", null, 3)).IsFalse();
        await Assert.That(CustomPropertyPatternPolicy.Matches("abc", null, null, 3)).IsTrue();
    }
}
