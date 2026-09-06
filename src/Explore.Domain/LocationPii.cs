// ABOUTME: Stores precise location-identifying fields in a dedicated extension table.
// ABOUTME: Restricts address and coordinate mutation to atomic Location aggregate transitions.

using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public class LocationPii
{
    private LocationPii()
    {
    }

    public Guid LocationId { get; private set; }
    public Location? Location { get; private set; }

    public string Address { get; private set; } = string.Empty;
    public string Postcode { get; private set; } = string.Empty;
    public string AddressSubstringKey { get; private set; } = string.Empty;
    public short AddressSubstringKeyVersion { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }

    public GeoCoordinate? GetCoordinate()
    {
        if (Latitude is not { } latitude || Longitude is not { } longitude)
        {
            return null;
        }

        return GeoCoordinate.TryCreate(latitude, longitude, out GeoCoordinate? coordinate)
            ? coordinate
            : null;
    }

    internal static LocationPii Create(string address, string postcode, GeoCoordinate? coordinate)
    {
        var pii = new LocationPii();
        pii.SetAddress(address, postcode, coordinate);
        return pii;
    }

    internal void SetAddress(string address, string postcode, GeoCoordinate? coordinate)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Address is required.", nameof(address));
        }
        if (string.IsNullOrWhiteSpace(postcode))
        {
            throw new ArgumentException("Postcode is required.", nameof(postcode));
        }

        string addressSubstringKey = LocationTextNormalization.Normalize(address);

        Address = address;
        Postcode = postcode;
        AddressSubstringKey = addressSubstringKey;
        AddressSubstringKeyVersion = LocationTextNormalization.CurrentRevision;
        Latitude = coordinate?.Latitude;
        Longitude = coordinate?.Longitude;
    }

    internal bool EnsureCurrentAddressSubstringKey()
    {
        string currentKey = LocationTextNormalization.Normalize(Address);
        if (HasCurrentAddressSubstringKey(currentKey))
        {
            return false;
        }

        SetCurrentAddressSubstringKey(currentKey);
        return true;
    }

    internal bool HasCurrentAddressSubstringKey(string currentKey) =>
        AddressSubstringKeyVersion == LocationTextNormalization.CurrentRevision
        && string.Equals(AddressSubstringKey, currentKey, StringComparison.Ordinal);

    internal void SetCurrentAddressSubstringKey(string currentKey)
    {
        AddressSubstringKey = currentKey;
        AddressSubstringKeyVersion = LocationTextNormalization.CurrentRevision;
    }

    internal void AssociateWith(Location location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Location = location;
        if (location.Id != Guid.Empty)
        {
            LocationId = location.Id;
        }
    }

    public override string ToString() => "LocationPii[redacted]";
}
