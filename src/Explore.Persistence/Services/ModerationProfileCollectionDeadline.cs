namespace Explore.Persistence.Services;

/// <summary>Retains the original collection deadline without limiting it to one native timer interval.</summary>
internal sealed class ModerationProfileCollectionDeadline : IDisposable
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly DateTimeOffset expiresAt;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ITimer timer;
    private bool disposed;

    public ModerationProfileCollectionDeadline(TimeProvider clock, DateTimeOffset expiresAt)
    {
        this.clock = clock;
        this.expiresAt = expiresAt;
        timer = clock.CreateTimer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Check();
    }

    public CancellationToken Token => cancellation.Token;

    private void Check()
    {
        lock (gate)
        {
            if (disposed) return;
            var remaining = expiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) cancellation.Cancel();
            else timer.Change(remaining < TimeSpan.FromDays(1) ? remaining : TimeSpan.FromDays(1), Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            timer.Dispose();
            cancellation.Dispose();
        }
    }
}
