namespace ISLAMU.DependencySubmission.Tests;

/// <summary>Timers fire only on explicit advancement; delay registration is an awaitable event.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ManualTimer> timers = [];
    private TimeSpan elapsed;
    private TaskCompletionSource<TimeSpan> nextDelay = NewSignal();
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (gate) return elapsed.Ticks; }
    public override DateTimeOffset GetUtcNow()
    {
        lock (gate) return new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero) + elapsed;
    }

    public Task<TimeSpan> NextDelay() { lock (gate) return nextDelay.Task; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (gate)
        {
            var timer = new ManualTimer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            if (dueTime > TimeSpan.Zero && dueTime != SubmissionClient.AttemptTimeout &&
                dueTime != SubmissionClient.OverallTimeout)
            {
                var signal = nextDelay;
                nextDelay = NewSignal();
                signal.TrySetResult(dueTime);
            }
            return timer;
        }
    }

    public void Advance(TimeSpan amount)
    {
        List<ManualTimer> due;
        lock (gate)
        {
            elapsed += amount;
            due = timers.Where(timer => !timer.Disposed && timer.Due <= elapsed).ToList();
            foreach (var timer in due)
                timer.Due = TimeSpan.MaxValue;
        }
        foreach (var timer in due)
            timer.Callback(timer.State);
    }

    private static TaskCompletionSource<TimeSpan> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        internal TimerCallback Callback { get; } = callback;
        internal object? State { get; } = state;
        internal TimeSpan Due { get; set; }
        internal bool Disposed { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                if (Disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : owner.elapsed + dueTime;
                return true;
            }
        }
        public void Dispose() { lock (owner.gate) Disposed = true; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
