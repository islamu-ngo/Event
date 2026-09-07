namespace Explore.Domain.Policies;

public sealed class RenderPolicy
{
    public PolicySlot<int> Version { get; set; } = new(1, ChildOverrideMode.Deny);
    public PolicySlot<string> Preset { get; set; } = new("AllInteractiveServer");
    public PolicySlot<bool> EnableAdvancedOverrides { get; set; } = new(false);
    public PolicySlot<string> GlobalRenderMode { get; set; } = new("InteractiveServer");
    public PolicySlot<bool> GlobalPrerenderEnabled { get; set; } = new(false);
    public PolicySlot<string> PublicSeoRenderMode { get; set; } = new("InteractiveServer");
    public PolicySlot<bool> PublicSeoPrerenderEnabled { get; set; } = new(false);
    public PolicySlot<string> OperationalRenderMode { get; set; } = new("InteractiveServer");
    public PolicySlot<bool> OperationalPrerenderEnabled { get; set; } = new(false);
    public PolicySlot<string> AdminRenderMode { get; set; } = new("InteractiveServer");
    public PolicySlot<bool> AdminPrerenderEnabled { get; set; } = new(false);
    public PolicySlot<string> OnboardingRenderMode { get; set; } = new("InteractiveServer");
    public PolicySlot<bool> OnboardingPrerenderEnabled { get; set; } = new(false);
    public PolicySlot<bool> DisallowInteractiveServerOnOnboarding { get; set; } = new(true, ChildOverrideMode.Deny);
    public PolicySlot<bool> AllowTenantOverride { get; set; } = new(false, ChildOverrideMode.Deny);
    public PolicySlot<bool> LockTenantPublicSeo { get; set; } = new(false, ChildOverrideMode.Deny);
    public PolicySlot<bool> LockTenantOperational { get; set; } = new(false, ChildOverrideMode.Deny);
    public PolicySlot<bool> LockTenantAdmin { get; set; } = new(false, ChildOverrideMode.Deny);
}
