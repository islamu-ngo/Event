namespace Explore.Domain.ValueObjects;

public sealed record GeoCoordinate
{
    private GeoCoordinate(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }
    public double Longitude { get; }

    public static GeoCoordinate Create(double latitude, double longitude)
    {
        if (!IsValidLatitude(latitude))
        {
            throw new ArgumentOutOfRangeException(
                nameof(latitude),
                "Latitude must be finite and between -90 and 90 degrees.");
        }

        if (!IsValidLongitude(longitude))
        {
            throw new ArgumentOutOfRangeException(
                nameof(longitude),
                "Longitude must be finite and between -180 and 180 degrees.");
        }

        return new GeoCoordinate(latitude, longitude);
    }

    internal static bool TryCreate(double latitude, double longitude, out GeoCoordinate? coordinate)
    {
        if (IsValidLatitude(latitude) && IsValidLongitude(longitude))
        {
            coordinate = new GeoCoordinate(latitude, longitude);
            return true;
        }

        coordinate = null;
        return false;
    }

    internal static bool IsValidLatitude(double latitude) =>
        double.IsFinite(latitude) && latitude is >= -90 and <= 90;

    internal static bool IsValidLongitude(double longitude) =>
        double.IsFinite(longitude) && longitude is >= -180 and <= 180;

    public override string ToString() => "GeoCoordinate[redacted]";
}
