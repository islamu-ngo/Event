using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Geocoding;

namespace Explore.Infrastructure.Geocoding;

internal static class PhotonGeoJsonParser
{
    private const int MaximumFeaturesInspected = 100;
    public static IReadOnlyList<ProtectedAddressSelection>? Parse(
        ReadOnlyMemory<byte> payload,
        int maximumResults,
        string datasetVersion)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("features", out JsonElement features)
                || features.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            List<ProtectedAddressSelection> suggestions = [];
            int inspected = 0;
            foreach (JsonElement feature in features.EnumerateArray())
            {
                if (++inspected > MaximumFeaturesInspected || suggestions.Count >= maximumResults)
                {
                    break;
                }

                if (TryMapFeature(
                        feature,
                        datasetVersion,
                        out ProtectedAddressSelection? suggestion)
                    && suggestion is not null)
                {
                    suggestions.Add(suggestion);
                }
            }

            return suggestions;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryMapFeature(
        JsonElement feature,
        string datasetVersion,
        out ProtectedAddressSelection? suggestion)
    {
        suggestion = null;
        if (feature.ValueKind != JsonValueKind.Object
            || !HasStringValue(feature, "type", "Feature")
            || !feature.TryGetProperty("geometry", out JsonElement geometry)
            || geometry.ValueKind != JsonValueKind.Object
            || !HasStringValue(geometry, "type", "Point")
            || !geometry.TryGetProperty("coordinates", out JsonElement coordinates)
            || coordinates.ValueKind != JsonValueKind.Array
            || coordinates.GetArrayLength() < 2
            || !coordinates[0].TryGetDouble(out double longitude)
            || !coordinates[1].TryGetDouble(out double latitude)
            || !IsCoordinateValid(latitude, longitude)
            || !feature.TryGetProperty("properties", out JsonElement properties)
            || properties.ValueKind != JsonValueKind.Object
            || !TryGetBoundedString(properties, "name", 300, required: true, out string displayName)
            || !TryGetBoundedString(properties, "street", 400, required: true, out string street)
            || !TryGetBoundedString(properties, "housenumber", 32, required: false, out string houseNumber)
            || !TryGetBoundedString(properties, "postcode", 32, required: false, out string postcode)
            || !TryGetBoundedString(properties, "city", 200, required: true, out string city)
            || !TryGetBoundedString(properties, "country", 200, required: true, out string country)
            || !TryGetBoundedString(properties, "osm_type", 16, required: false, out string recordType)
            || !TryGetBoundedString(properties, "osm_id", 128, required: false, out string recordId))
        {
            return false;
        }

        string address = string.IsNullOrEmpty(houseNumber) ? street : $"{street} {houseNumber}";
        suggestion = new ProtectedAddressSelection
        {
            DisplayName = displayName,
            Address = address,
            Postcode = postcode,
            City = city,
            Country = country,
            Latitude = latitude,
            Longitude = longitude,
            Attribution = PhotonProvenance.Attribution,
            Provenance = new ProtectedAddressProvenance
            {
                Provider = PhotonProvenance.Provider,
                ProviderRecordId = string.IsNullOrEmpty(recordType)
                    ? recordId
                    : $"{recordType}:{recordId}",
                DatasetVersion = datasetVersion
            }
        };
        return true;
    }

    private static bool HasStringValue(JsonElement parent, string name, string expected) =>
        parent.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
        && string.Equals(property.GetString(), expected, StringComparison.Ordinal);

    private static bool TryGetBoundedString(
        JsonElement parent,
        string name,
        int maximumLength,
        bool required,
        out string value)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(name, out JsonElement property))
        {
            return !required;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? raw = property.GetString();
        if (raw is null)
        {
            return !required;
        }

        value = raw.Trim();
        return value.Length <= maximumLength && (!required || value.Length > 0);
    }

    private static bool IsCoordinateValid(double latitude, double longitude) =>
        double.IsFinite(latitude)
        && double.IsFinite(longitude)
        && latitude is >= -90 and <= 90
        && longitude is >= -180 and <= 180;
}
