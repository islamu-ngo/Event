namespace Explore.Application.Services;

/// <summary>
/// Keeps a resource projection private through asynchronous representation preparation.
/// Only one terminal consumer can take it, and fresh authority precedes delivery.
/// </summary>
public sealed class EventResourceReadProjection<T> : IAsyncDisposable where T : class
{
    private OwnedValue? _value;
    private readonly Func<CancellationToken, Task<EventResourceAuthorityOutcome>> _authorize;
    private readonly CancellationToken _originCancellation;

    internal EventResourceReadProjection(T value,
        Func<CancellationToken, Task<EventResourceAuthorityOutcome>> authorize,
        CancellationToken originCancellation, IAsyncDisposable? owner = null)
        => (_value, _authorize, _originCancellation) = (new(value, owner), authorize, originCancellation);

    public async Task<EventResourceAuthorityOutcome> DiscloseAsync<TRepresentation>(
        Func<T, CancellationToken, Task<TRepresentation>> prepare,
        Func<TRepresentation, CancellationToken, Task> deliver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(deliver);
        var value = Interlocked.Exchange(ref _value, null);
        if (value is null) return EventResourceAuthorityOutcome.NotFound;
        await using var owner = value.Owner;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _originCancellation, cancellationToken);
        var token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        var representation = await prepare(value.Value, token);
        token.ThrowIfCancellationRequested();
        var outcome = await _authorize(token);
        token.ThrowIfCancellationRequested();
        if (outcome != EventResourceAuthorityOutcome.Allowed) return outcome;
        await deliver(representation, token);
        token.ThrowIfCancellationRequested();
        return EventResourceAuthorityOutcome.Allowed;
    }

    public override string ToString() => nameof(EventResourceReadProjection<T>);

    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _value, null)?.Owner?.DisposeAsync() ?? ValueTask.CompletedTask;

    private sealed record OwnedValue(T Value, IAsyncDisposable? Owner);
}
