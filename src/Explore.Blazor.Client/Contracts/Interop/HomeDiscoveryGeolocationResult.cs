using System.Text.Json.Serialization;

namespace Explore.Blazor.Client.Contracts.Interop;

public sealed record HomeDiscoveryGeolocationResult(
    HomeDiscoveryGeolocationStatus Status,
    double? Latitude = null,
    double? Longitude = null);

[JsonConverter(typeof(JsonStringEnumConverter<HomeDiscoveryGeolocationStatus>))]
public enum HomeDiscoveryGeolocationStatus
{
    Available,
    Denied,
    Unavailable
}
