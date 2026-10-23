using System.Text.Json.Serialization;

namespace Explore.Application.Services;

/// <summary>A native workflow result, not a wire DTO. Denied reads never carry a representation.</summary>
public sealed record EventResourceManagementReadResult<T> where T : class
{
    public EventResourceAuthorityOutcome Outcome { get; }
    [JsonIgnore]
    public EventResourceReadProjection<T>? Projection { get; }
    internal EventResourceManagementReadResult(EventResourceAuthorityOutcome outcome, EventResourceReadProjection<T>? projection)
        => (Outcome, Projection) = (outcome, projection);
    internal EventResourceManagementReadResult(T value,
        Func<CancellationToken, Task<EventResourceAuthorityOutcome>> authorize,
        CancellationToken cancellationToken)
    {
        Outcome = EventResourceAuthorityOutcome.Allowed;
        Projection = new(value ?? throw new ArgumentNullException(nameof(value)), authorize, cancellationToken);
    }
    public override string ToString() => nameof(EventResourceManagementReadResult<T>);
}

public static class EventResourceManagementReadResult
{
    public static EventResourceManagementReadResult<T> Success<T>(T value,
        Func<CancellationToken, Task<EventResourceAuthorityOutcome>> authorize,
        CancellationToken cancellationToken) where T : class =>
        new(value, authorize, cancellationToken);
    public static EventResourceManagementReadResult<T> Denied<T>(EventResourceAuthorityOutcome outcome) where T : class =>
        outcome == EventResourceAuthorityOutcome.Allowed
            ? throw new ArgumentException("A denial needs a failure outcome.", nameof(outcome)) : new(outcome, null);
}
