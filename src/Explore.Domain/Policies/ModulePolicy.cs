namespace Explore.Domain.Policies;

public sealed class ModulePolicy
{
    public PolicySlot<bool> EnableIslamicModule { get; set; } = new(true, ChildOverrideMode.Deny);
    public PolicySlot<bool> EnableTechModule { get; set; } = new(true, ChildOverrideMode.Deny);
}
