using System.Text.Json.Serialization;

namespace Explore.Application.Models.PublicExperience;

[JsonConverter(typeof(JsonStringEnumConverter<HomeDiscoveryMode>))]
public enum HomeDiscoveryMode
{
    Area = 0,
    Online = 1,
    All = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<HomeDiscoverySectionStatus>))]
public enum HomeDiscoverySectionStatus
{
    Available = 0,
    Empty = 1,
    Failed = 2,
    Omitted = 3
}
