namespace ISLAMU.Event.Setup.Core;

public enum SetupReadinessState
{
    Ready,
    Incomplete,
    Blocked
}

public sealed record SetupReadinessInput
{
    public SetupReadinessInput(
        IEnumerable<PortableSectionKey> required,
        IEnumerable<PortableSectionKey> selected,
        IEnumerable<PortableSectionKey> blocked)
    {
        Required = SetupSnapshot.OrderedDistinct(required, static item => item.Value, nameof(required));
        Selected = SetupSnapshot.OrderedDistinct(selected, static item => item.Value, nameof(selected));
        Blocked = SetupSnapshot.OrderedDistinct(blocked, static item => item.Value, nameof(blocked));
    }

    public IReadOnlyList<PortableSectionKey> Required { get; }
    public IReadOnlyList<PortableSectionKey> Selected { get; }
    public IReadOnlyList<PortableSectionKey> Blocked { get; }
}

public sealed record SetupReadinessResult
{
    internal SetupReadinessResult(
        SetupReadinessState state,
        IReadOnlyList<PortableSectionKey> missing,
        IReadOnlyList<PortableSectionKey> blocked)
    {
        State = state;
        Missing = missing;
        Blocked = blocked;
    }

    public SetupReadinessState State { get; }
    public IReadOnlyList<PortableSectionKey> Missing { get; }
    public IReadOnlyList<PortableSectionKey> Blocked { get; }
}

public static class SetupReadiness
{
    public static SetupReadinessResult Evaluate(SetupReadinessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        HashSet<PortableSectionKey> selected = input.Selected.ToHashSet();
        PortableSectionKey[] missing = input.Required
            .Where(item => !selected.Contains(item))
            .ToArray();
        SetupReadinessState state = input.Blocked.Count > 0
            ? SetupReadinessState.Blocked
            : missing.Length > 0
                ? SetupReadinessState.Incomplete
                : SetupReadinessState.Ready;
        return new SetupReadinessResult(state, Array.AsReadOnly(missing), input.Blocked);
    }
}
