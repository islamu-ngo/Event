namespace Explore.Application.Services;

/// <summary>
/// Keeps a resource projection private through asynchronous representation preparation.
/// Only one terminal consumer can take it, and fresh authority precedes delivery.
/// </summary>
public sealed class EventResourceReadProjection<T> where T : class
{
    private T? _value;
    private readonly Func<CancellationToken, Task<EventResourceAuthorityOutcome>> _authorize;
    private readonly CancellationToken _originCancellation;

    internal EventResourceReadProjection(T value,
        Func<CancellationToken, Task<EventResourceAuthorityOutcome>> authorize,
        CancellationToken originCancellation)
        => (_value, _authorize, _originCancellation) = (value, authorize, originCancellation);

    public async Task<EventResourceAuthorityOutcome> DiscloseAsync<TRepresentation>(
        Func<T, CancellationToken, Task<TRepresentation>> prepare,
        Func<TRepresentation, CancellationToken, Task> deliver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(deliver);
        var value = Interlocked.Exchange(ref _value, null);
        if (value is null) return EventResourceAuthorityOutcome.NotFound;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _originCancellation, cancellationToken);
        var token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        var representation = await prepare(value, token);
        token.ThrowIfCancellationRequested();
        var outcome = await _authorize(token);
        token.ThrowIfCancellationRequested();
        if (outcome != EventResourceAuthorityOutcome.Allowed) return outcome;
        await deliver(representation, token);
        return EventResourceAuthorityOutcome.Allowed;
    }

    public override string ToString() => nameof(EventResourceReadProjection<T>);
}
