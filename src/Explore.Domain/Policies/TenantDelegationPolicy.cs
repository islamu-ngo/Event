namespace Explore.Domain.Policies;

public sealed class TenantDelegationPolicy
{
    public PolicySlot<bool> AllowSelfServiceRegistration { get; set; } = new(false);
    public PolicySlot<bool> AllowWhiteLabeling { get; set; } = new(false);
    public PolicySlot<string> DefaultPublicHomePage { get; set; } = new("EventList");
    public PolicySlot<bool> LockTenantSmtp { get; set; } = new(true, ChildOverrideMode.Deny);
    public PolicySlot<bool> LockTenantStorage { get; set; } = new(true, ChildOverrideMode.Deny);
    public PolicySlot<bool> LockTenantAnalytics { get; set; } = new(true, ChildOverrideMode.Deny);
    public PolicySlot<string> AuthorizationProvider { get; set; } = new("local", ChildOverrideMode.Deny);
}
