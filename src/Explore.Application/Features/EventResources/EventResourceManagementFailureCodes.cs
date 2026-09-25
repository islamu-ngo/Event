namespace Explore.Application.Features.EventResources;

public static class EventResourceManagementFailureCodes
{
    public const string Forbidden = "event_resource_forbidden";
    public const string Unavailable = "event_resource_unavailable";
    public const string PublicationUnavailable = "event_resource_publication_unavailable";
    public const string CapacityExceeded = "event_resource_capacity_exceeded";
}
