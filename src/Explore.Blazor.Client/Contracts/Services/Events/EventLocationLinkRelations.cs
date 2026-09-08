namespace Explore.Blazor.Client.Contracts.Services.Events;

/// <summary>
/// HAL relations the EventLocation surface publishes. Components compare against these constants
/// instead of literals so a renamed server relation fails to compile rather than silently hiding
/// or exposing an action.
/// </summary>
public static class EventLocationLinkRelations
{
    public const string Self = "self";
    public const string Edit = "edit";
    public const string Remediate = "remediate-location";
}
