namespace Explore.Infrastructure.Tests.Geocoding;

internal sealed class PhotonManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly Dictionary<long, List<TaskCompletionSource>> _scheduled = [];
    private DateTimeOffset _utcNow = initialUtcNow;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _utcNow;
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_lock)
        {
            return _utcNow.UtcTicks;
        }
    }

    public Task ExpectDelay(TimeSpan dueTime)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (!_scheduled.TryGetValue(dueTime.Ticks, out List<TaskCompletionSource>? waiters))
            {
                waiters = [];
                _scheduled.Add(dueTime.Ticks, waiters);
            }

            waiters.Add(completion);
        }

        return completion.Task;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (_lock)
        {
            _timers.Add(timer);
            SignalScheduled(dueTime);
        }

        return timer;
    }

    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        DateTimeOffset target;
        lock (_lock)
        {
            target = _utcNow + amount;
        }

        while (true)
        {
            ManualTimer? due;
            lock (_lock)
            {
                due = _timers
                    .Where(timer => timer.IsDueAtOrBefore(target))
                    .OrderBy(timer => timer.DueAtUtc)
                    .FirstOrDefault();
                if (due is null)
                {
                    _utcNow = target;
                    return;
                }

                _utcNow = due.DueAtUtc;
                due.PrepareForCallback();
            }

            due.Invoke();
        }
    }

    private void SignalScheduled(TimeSpan dueTime)
    {
        if (!_scheduled.TryGetValue(dueTime.Ticks, out List<TaskCompletionSource>? waiters)
            || waiters.Count == 0)
        {
            return;
        }

        TaskCompletionSource waiter = waiters[0];
        waiters.RemoveAt(0);
        waiter.TrySetResult();
    }

    private sealed class ManualTimer(
        PhotonManualTimeProvider owner,
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) : ITimer
    {
        private bool _disposed;
        private TimeSpan _period = period;

        public DateTimeOffset DueAtUtc { get; private set; } = owner.GetUtcNow() + dueTime;

        public bool IsDueAtOrBefore(DateTimeOffset target) => !_disposed && DueAtUtc <= target;

        public void PrepareForCallback()
        {
            if (_period == Timeout.InfiniteTimeSpan)
            {
                _disposed = true;
                return;
            }

            DueAtUtc += _period;
        }

        public void Invoke() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._lock)
            {
                if (_disposed)
                {
                    return false;
                }

                DueAtUtc = owner._utcNow + dueTime;
                _period = period;
                owner.SignalScheduled(dueTime);
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._lock)
            {
                _disposed = true;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
