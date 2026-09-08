using Explore.Application.Helpers;

namespace Event.Application.UnitTests.Helpers;

public class AttributeResolverTests
{
    [Test]
    public async Task TryGetGuid_AcceptsGuidAndStringRepresentations()
    {
        var expected = Guid.NewGuid();

        var typedResult = AttributeResolver.TryGetGuid(expected, out var typedValue);
        var stringResult = AttributeResolver.TryGetGuid(expected.ToString("D"), out var stringValue);

        await Assert.That(typedResult).IsTrue();
        await Assert.That(typedValue).IsEqualTo(expected);
        await Assert.That(stringResult).IsTrue();
        await Assert.That(stringValue).IsEqualTo(expected);
    }

    [Test]
    public async Task TryGetGuid_RejectsInvalidMissingAndMismatchedValues()
    {
        foreach (var value in new object?[] { null, "not-a-guid", 42 })
        {
            var result = AttributeResolver.TryGetGuid(value, out var resolved);

            await Assert.That(result).IsFalse();
            await Assert.That(resolved).IsEqualTo(Guid.Empty);
        }
    }

    [Test]
    public async Task TryGetInt_AcceptsIntLongAndStringRepresentations()
    {
        foreach (var value in new object[] { 42, 42L, "42" })
        {
            var result = AttributeResolver.TryGetInt(value, out var resolved);

            await Assert.That(result).IsTrue();
            await Assert.That(resolved).IsEqualTo(42);
        }
    }

    [Test]
    public async Task TryGetInt_RejectsInvalidMissingMismatchedAndOutOfRangeValues()
    {
        foreach (var value in new object?[] { null, "not-an-int", true, (long)int.MaxValue + 1 })
        {
            var result = AttributeResolver.TryGetInt(value, out var resolved);

            await Assert.That(result).IsFalse();
            await Assert.That(resolved).IsEqualTo(0);
        }
    }
}
