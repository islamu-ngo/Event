using System.Globalization;

namespace Explore.Infrastructure.Tests.Geocoding;

internal static class PhotonGeoJsonFixtures
{
    public const string Empty = "{\"type\":\"FeatureCollection\",\"features\":[]}";

    public static string Feature(
        string name,
        string street,
        string houseNumber,
        string postcode,
        double longitude,
        double latitude,
        string recordId) => $$"""
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "geometry": { "type": "Point", "coordinates": [{{Invariant(longitude)}}, {{Invariant(latitude)}}] },
              "properties": {
                "name": "{{name}}",
                "street": "{{street}}",
                "housenumber": "{{houseNumber}}",
                "postcode": "{{postcode}}",
                "city": "Brussels",
                "country": "Belgium",
                "osm_type": "W",
                "osm_id": "{{recordId}}",
                "unknown_provider_field": "ignored-canary"
              }
            }
          ]
        }
        """;

    public static string Features(params string[] featureBodies) => $$"""
        {
          "type": "FeatureCollection",
          "features": [{{string.Join(',', featureBodies)}}]
        }
        """;

    public static string FeatureBody(
        string name,
        double longitude,
        double latitude,
        string recordId) => $$"""
        {
          "type": "Feature",
          "geometry": { "type": "Point", "coordinates": [{{Invariant(longitude)}}, {{Invariant(latitude)}}] },
          "properties": {
            "name": "{{name}}",
            "street": "Contract Street",
            "housenumber": "30",
            "postcode": "1000",
            "city": "Brussels",
            "country": "Belgium",
            "osm_type": "N",
            "osm_id": "{{recordId}}"
          }
        }
        """;

    private static string Invariant(double value) => value.ToString(CultureInfo.InvariantCulture);
}
