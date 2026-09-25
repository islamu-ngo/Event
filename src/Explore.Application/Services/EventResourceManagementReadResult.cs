namespace Explore.Application.Services;

/// <summary>A native workflow result, not a wire DTO. Denied reads never carry a representation.</summary>
public sealed record EventResourceManagementReadResult<T> where T : class
{
    public EventResourceAuthorityOutcome Outcome { get; }
    public T? Value { get; }
    internal EventResourceManagementReadResult(EventResourceAuthorityOutcome outcome, T? value)
        => (Outcome, Value) = (outcome, value);
    public override string ToString() => nameof(EventResourceManagementReadResult<T>);
}

public static class EventResourceManagementReadResult
{
    public static EventResourceManagementReadResult<T> Success<T>(T value) where T : class =>
        new(EventResourceAuthorityOutcome.Allowed, value ?? throw new ArgumentNullException(nameof(value)));
    public static EventResourceManagementReadResult<T> Denied<T>(EventResourceAuthorityOutcome outcome) where T : class =>
        outcome == EventResourceAuthorityOutcome.Allowed
            ? throw new ArgumentException("A denial needs a failure outcome.", nameof(outcome)) : new(outcome, null);
}
