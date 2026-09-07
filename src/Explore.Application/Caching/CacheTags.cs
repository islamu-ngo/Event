namespace Explore.Application.Caching;

public static class CacheTags
{
    public const string Events = "events";
    public const string EventLists = "events:list";
    public const string EventDetails = "events:detail";
    public const string EventLocations = "event-locations";
    public const string Categories = "categories";
    public const string CategoryLists = "categories:list";
    public const string CategoryDetails = "categories:detail";
    public const string Groups = "groups";
    public const string GroupLists = "groups:list";
    public const string GroupDetails = "groups:detail";
    public const string Organizations = "organizations";
    public const string OrganizationLists = "organizations:list";
    public const string OrganizationDetails = "organizations:detail";

    public static string Event(Guid eventId) => $"event:{eventId}";
    public static string EventListByTenant(Guid tenantId) => $"events:list:tenant:{tenantId:N}";
    public static string EventLocationsByTenant(Guid tenantId) => $"event-locations:tenant:{tenantId:N}";
    public static string EventLocationsByEvent(Guid eventId) => $"event-locations:event:{eventId:N}";
    public static string EventLocation(Guid eventLocationId) => $"event-location:{eventLocationId:N}";
    public static string Group(Guid groupId) => $"group:{groupId}";
    public static string Organization(Guid organizationId) => $"organization:{organizationId}";
}
