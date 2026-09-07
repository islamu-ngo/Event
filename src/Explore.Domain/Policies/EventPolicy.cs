namespace Explore.Domain.Policies;

public sealed class EventPolicy
{
    public PolicySlot<bool> AllowUserSubmittedEvents { get; set; } = new(true);
    public PolicySlot<bool> AllowOrganizationSubmittedEvents { get; set; } = new(true);
    public PolicySlot<bool> AllowGroupSubmittedEvents { get; set; } = new(true);
    public PolicySlot<bool> EventCardClickOpensDetailPage { get; set; } = new(false);
}
