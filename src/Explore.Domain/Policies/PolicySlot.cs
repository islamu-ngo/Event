namespace Explore.Domain.Policies;

public sealed class PolicySlot<T>
{
    public T? LocalValue { get; set; }
    public ChildOverrideMode OverrideMode { get; set; } = ChildOverrideMode.Allow;

    public PolicySlot() { }

    public PolicySlot(T? localValue, ChildOverrideMode overrideMode = ChildOverrideMode.Allow)
    {
        LocalValue = localValue;
        OverrideMode = overrideMode;
    }
}

public enum ChildOverrideMode
{
    Allow = 0,
    Deny = 1
}
