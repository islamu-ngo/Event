using System.Reflection;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class LocalDateRangeTests
{
    private static readonly DateOnly First = new(2026, 8, 25);
    private static readonly DateOnly Second = new(2026, 8, 26);
    private static readonly DateOnly Third = new(2026, 8, 27);

    [Test]
    public async Task CreateRejectsStartAfterEnd()
    {
        await Assert.That(() => LocalDateRange.Create(Second, First))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task EqualDatesCreateOneInclusiveDay()
    {
        LocalDateRange range = LocalDateRange.Create(First, First);

        await Assert.That(range.Start).IsEqualTo(First);
        await Assert.That(range.End).IsEqualTo(First);
        await Assert.That(range.Contains(First)).IsTrue();
    }

    [Test]
    public async Task ContainsIncludesBothBoundaries()
    {
        LocalDateRange range = LocalDateRange.Create(First, Third);

        await Assert.That(range.Contains(First)).IsTrue();
        await Assert.That(range.Contains(Second)).IsTrue();
        await Assert.That(range.Contains(Third)).IsTrue();
        await Assert.That(range.Contains(First.AddDays(-1))).IsFalse();
        await Assert.That(range.Contains(Third.AddDays(1))).IsFalse();
    }

    [Test]
    public async Task OverlapsTreatsTouchingInclusiveRangesAsOverlapping()
    {
        LocalDateRange range = LocalDateRange.Create(First, Second);
        LocalDateRange touching = LocalDateRange.Create(Second, Third);
        LocalDateRange separate = LocalDateRange.Create(Third, Third);

        await Assert.That(range.Overlaps(touching)).IsTrue();
        await Assert.That(range.Overlaps(separate)).IsFalse();
    }

    [Test]
    public async Task EqualityAndFormattingUseExactEndpoints()
    {
        LocalDateRange range = LocalDateRange.Create(First, Third);
        LocalDateRange equal = LocalDateRange.Create(First, Third);
        LocalDateRange different = LocalDateRange.Create(Second, Third);

        await Assert.That(range).IsEqualTo(equal);
        await Assert.That(range.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(range).IsNotEqualTo(different);
        await Assert.That(range.ToString()).IsEqualTo("2026-08-25/2026-08-27");
    }

    [Test]
    public async Task SurfaceHasNoTimezoneTypesPublicConstructionOrConversions()
    {
        PropertyInfo[] timezoneProperties = typeof(LocalDateRange)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(DateTime)
                || property.PropertyType == typeof(DateTimeOffset)
                || property.PropertyType == typeof(TimeZoneInfo))
            .ToArray();
        MethodInfo[] conversions = typeof(LocalDateRange)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit")
            .ToArray();

        await Assert.That(typeof(LocalDateRange).IsClass).IsTrue();
        await Assert.That(typeof(LocalDateRange).IsSealed).IsTrue();
        await Assert.That(typeof(LocalDateRange).GetConstructors()).IsEmpty();
        await Assert.That(timezoneProperties).IsEmpty();
        await Assert.That(conversions).IsEmpty();
    }
}
