using System.Globalization;
using System.Reflection;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class GeoCoordinateTests
{
    [Test]
    [Arguments(-90.0, -180.0)]
    [Arguments(0.0, 0.0)]
    [Arguments(90.0, 180.0)]
    public async Task CreatePreservesValidCoordinatesWithoutClamping(double latitude, double longitude)
    {
        GeoCoordinate coordinate = GeoCoordinate.Create(latitude, longitude);

        await Assert.That(coordinate.Latitude).IsEqualTo(latitude);
        await Assert.That(coordinate.Longitude).IsEqualTo(longitude);
    }

    [Test]
    [Arguments(-90.000_001, 0.0)]
    [Arguments(90.000_001, 0.0)]
    [Arguments(0.0, -180.000_001)]
    [Arguments(0.0, 180.000_001)]
    public async Task CreateRejectsOutOfRangeCoordinates(double latitude, double longitude)
    {
        await Assert.That(() => GeoCoordinate.Create(latitude, longitude))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CreateRejectsNonFiniteCoordinates()
    {
        double[] invalidValues = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];

        foreach (double invalid in invalidValues)
        {
            await Assert.That(() => GeoCoordinate.Create(invalid, 0)).Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => GeoCoordinate.Create(0, invalid)).Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task EqualityUsesExactCoordinateValues()
    {
        GeoCoordinate coordinate = GeoCoordinate.Create(12.345_678, -45.678_912);
        GeoCoordinate equal = GeoCoordinate.Create(12.345_678, -45.678_912);
        GeoCoordinate different = GeoCoordinate.Create(12.345_679, -45.678_912);

        await Assert.That(coordinate).IsEqualTo(equal);
        await Assert.That(coordinate.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(coordinate).IsNotEqualTo(different);
    }

    [Test]
    public async Task FormattingIsCultureInvariantAndOmitsExactCoordinates()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-BE");
            string formatted = GeoCoordinate.Create(12.345_678, -45.678_912).ToString();

            await Assert.That(formatted).IsEqualTo("GeoCoordinate[redacted]");
            await Assert.That(formatted).DoesNotContain("12");
            await Assert.That(formatted).DoesNotContain("45");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    public async Task SurfaceHasNoPublicConstructionOrConversions()
    {
        MethodInfo[] conversions = typeof(GeoCoordinate)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit")
            .ToArray();

        await Assert.That(typeof(GeoCoordinate).IsClass).IsTrue();
        await Assert.That(typeof(GeoCoordinate).IsSealed).IsTrue();
        await Assert.That(typeof(GeoCoordinate).GetConstructors()).IsEmpty();
        await Assert.That(conversions).IsEmpty();
    }
}
