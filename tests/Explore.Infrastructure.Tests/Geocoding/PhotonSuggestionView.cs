using System.Reflection;

namespace Explore.Infrastructure.Tests.Geocoding;

internal sealed record PhotonSuggestionView(
    string DisplayName,
    string Address,
    string Postcode,
    double Latitude,
    double Longitude,
    string Provider,
    string ProviderRecordId,
    string Attribution,
    string? DatasetVersion)
{
    public static PhotonSuggestionView From(object suggestion)
    {
        object? coordinate = GetOptional(suggestion, "Coordinate");
        object? provenance = GetOptional(suggestion, "Provenance");
        return new PhotonSuggestionView(
            GetRequired<string>(suggestion, "DisplayName"),
            GetRequired<string>(suggestion, "Address"),
            GetRequired<string>(suggestion, "Postcode"),
            GetCoordinate(suggestion, coordinate, "Latitude"),
            GetCoordinate(suggestion, coordinate, "Longitude"),
            provenance is null
                ? GetRequired<string>(suggestion, "Provider", "Source")
                : GetRequired<string>(provenance, "Provider", "Source"),
            provenance is null
                ? GetRequired<string>(suggestion, "ProviderRecordId", "RecordId")
                : GetRequired<string>(provenance, "ProviderRecordId", "RecordId"),
            GetRequired<string>(suggestion, "Attribution"),
            (provenance is null
                ? GetOptional(suggestion, "DatasetVersion")
                : GetOptional(provenance, "DatasetVersion"))?.ToString());
    }

    private static double GetCoordinate(object suggestion, object? coordinate, string name)
    {
        object? value = GetOptional(suggestion, name) ?? (coordinate is null ? null : GetOptional(coordinate, name));
        return value is null
            ? throw PhotonAdapterContractHost.Red($"Provider suggestion must expose {name}.")
            : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static T GetRequired<T>(object target, params string[] names)
    {
        object? value = names.Select(name => GetOptional(target, name)).FirstOrDefault(item => item is not null);
        return value is T typed
            ? typed
            : throw PhotonAdapterContractHost.Red(
                $"{target.GetType().FullName} must expose {string.Join(" or ", names)} as {typeof(T).Name}.");
    }

    private static object? GetOptional(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
}
