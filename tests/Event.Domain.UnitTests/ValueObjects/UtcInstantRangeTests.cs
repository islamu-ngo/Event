using System.Reflection;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class UtcInstantRangeTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 25, 10, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset End =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.FromHours(2));

    [Test]
    public async Task CreateRejectsEqualOrDescendingInstants()
    {
        await Assert.That(() => UtcInstantRange.Create(Start, Start))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => UtcInstantRange.Create(End, Start))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CreateNormalizesEndpointsToUtc()
    {
        UtcInstantRange range = UtcInstantRange.Create(Start, End);

        await Assert.That(range.Start.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(range.End.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(range.Start).IsEqualTo(Start.ToUniversalTime());
        await Assert.That(range.End).IsEqualTo(End.ToUniversalTime());
    }

    [Test]
    public async Task EqualityUsesNormalizedInstants()
    {
        UtcInstantRange range = UtcInstantRange.Create(Start, End);
        UtcInstantRange equal = UtcInstantRange.Create(
            Start.ToOffset(TimeSpan.FromHours(-4)),
            End.ToOffset(TimeSpan.FromHours(-4)));

        await Assert.That(range).IsEqualTo(equal);
        await Assert.That(range.GetHashCode()).IsEqualTo(equal.GetHashCode());
    }

    [Test]
    public async Task ContainsUsesHalfOpenBoundaries()
    {
        UtcInstantRange range = UtcInstantRange.Create(Start, End);

        await Assert.That(range.Contains(Start)).IsTrue();
        await Assert.That(range.Contains(End.AddMinutes(-1))).IsTrue();
        await Assert.That(range.Contains(End)).IsFalse();
    }

    [Test]
    public async Task OverlapsAllowsAdjacentNonOverlappingRanges()
    {
        UtcInstantRange range = UtcInstantRange.Create(Start, End);
        UtcInstantRange overlapping = UtcInstantRange.Create(End.AddMinutes(-1), End.AddHours(1));
        UtcInstantRange adjacent = UtcInstantRange.Create(End, End.AddHours(1));

        await Assert.That(range.Overlaps(overlapping)).IsTrue();
        await Assert.That(range.Overlaps(adjacent)).IsFalse();
    }

    [Test]
    public async Task FormattingUsesInvariantRoundTripUtcValues()
    {
        UtcInstantRange range = UtcInstantRange.Create(Start, End);

        await Assert.That(range.ToString())
            .IsEqualTo("2026-08-25T08:00:00.0000000+00:00/2026-08-25T10:00:00.0000000+00:00");
    }

    [Test]
    public async Task SurfaceHasNoLocalDatePublicConstructionOrConversions()
    {
        PropertyInfo[] localDateProperties = typeof(UtcInstantRange)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(DateOnly)
                || property.PropertyType == typeof(TimeOnly)
                || property.PropertyType == typeof(DateTime))
            .ToArray();
        MethodInfo[] conversions = typeof(UtcInstantRange)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit")
            .ToArray();

        await Assert.That(typeof(UtcInstantRange).IsClass).IsTrue();
        await Assert.That(typeof(UtcInstantRange).IsSealed).IsTrue();
        await Assert.That(typeof(UtcInstantRange).GetConstructors()).IsEmpty();
        await Assert.That(localDateProperties).IsEmpty();
        await Assert.That(conversions).IsEmpty();
    }
}
