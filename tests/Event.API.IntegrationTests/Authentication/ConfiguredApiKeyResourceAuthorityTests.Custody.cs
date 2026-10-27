using Explore.API.Authentication;
using Explore.Application.Constants;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Event.API.IntegrationTests.Authentication;

public sealed partial class ConfiguredApiKeyResourceAuthorityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualExpiryInvalidatesRetainedSourceWithoutRenewingLongDeadlines(bool longDeadline)
    {
        var client = Client();
        client.ExpiresAtUtc = Now + (longDeadline ? TimeSpan.FromDays(100) : TimeSpan.FromMinutes(2));
        using var source = new CredentialOptions(new() { Clients = [client] });
        var clock = new CustodyClock();
        var reader = SourceReader(source.Monitor, clock);
        await using var lease = await reader.AcquireAsync(client.KeyId, default);
        await Assert.That(lease).IsNotNull();
        await Assert.That(lease!.ExpiresAtUtc).IsEqualTo(client.ExpiresAtUtc);
        await Assert.That(await lease.RevalidateAsync(default)).IsTrue();
        var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = lease.ValidityCancellation.Register(() => invalidated.TrySetResult());
        if (longDeadline)
        {
            clock.Advance(TimeSpan.FromDays(50));
            await Assert.That(lease.ValidityCancellation.IsCancellationRequested).IsFalse();
            await Assert.That(await lease.RevalidateAsync(default)).IsTrue();
            clock.Advance(TimeSpan.FromDays(50));
        }
        else clock.Advance(TimeSpan.FromMinutes(2));
        await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(await lease.RevalidateAsync(default)).IsFalse();
        await Assert.That(lease.ExpiresAtUtc).IsEqualTo(client.ExpiresAtUtc);
    }

    [Test]
    public async Task BackwardWallClockCannotExtendTheOriginallyCapturedExpiryBudget()
    {
        var client = Client();
        client.ExpiresAtUtc = Now.AddMinutes(2);
        using var source = new CredentialOptions(new() { Clients = [client] });
        var clock = new CustodyClock();
        var reader = SourceReader(source.Monitor, clock);
        await using var lease = await reader.AcquireAsync(client.KeyId, default);
        await Assert.That(lease).IsNotNull();
        clock.Advance(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(-1));
        await Assert.That(clock.GetUtcNow()).IsLessThan(client.ExpiresAtUtc!.Value);
        await Assert.That(lease!.ValidityCancellation.IsCancellationRequested).IsTrue();
        await Assert.That(await lease.RevalidateAsync(default)).IsFalse();
    }

    [Test]
    public async Task IdenticalReplacementMetadataCannotBecomeTheOriginalSource()
    {
        var client = Client();
        using var source = new CredentialOptions(new() { Clients = [client] });
        var reader = SourceReader(source.Monitor, new CustodyClock());
        await using var original = await reader.AcquireAsync(client.KeyId, default);
        await Assert.That(original).IsNotNull();
        var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = original!.ValidityCancellation.Register(() => invalidated.TrySetResult());
        source.Replace(new() { Clients = [client] });
        await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(await original.RevalidateAsync(default)).IsFalse();
        await using var replacement = await reader.AcquireAsync(client.KeyId, default);
        await Assert.That(replacement).IsNotNull();
        await Assert.That(await replacement!.RevalidateAsync(default)).IsTrue();
        await Assert.That(await original.RevalidateAsync(default)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReplacementAcrossSubscriptionCannotIssueCustodyOfTheOldSource(bool beforeSubscription)
    {
        var client = Client();
        using var source = new CredentialOptions(new() { Clients = [client] });
        var monitor = new SubscriptionBoundary(source, beforeSubscription,
            new() { Clients = [client] });
        var reader = SourceReader(monitor, new CustodyClock());
        await Assert.That(await reader.AcquireAsync(client.KeyId, default)).IsNull();
    }

    [Test]
    [Arguments("owner")]
    [Arguments("scopes")]
    [Arguments("expiry")]
    public async Task InPlaceDescriptorChangesCannotReplaceTheCapturedSource(string change)
    {
        var client = Client();
        using var source = new CredentialOptions(new() { Clients = [client] });
        var reader = SourceReader(source.Monitor, new CustodyClock());
        await using var lease = await reader.AcquireAsync(client.KeyId, default);
        await Assert.That(lease).IsNotNull();
        switch (change)
        {
            case "owner": client.OwnerId = Guid.CreateVersion7().ToString(); break;
            case "scopes": client.Scopes.Clear(); break;
            case "expiry": client.ExpiresAtUtc = Now.AddDays(2); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        await Assert.That(await lease!.RevalidateAsync(default)).IsFalse();
        await Assert.That(lease.ValidityCancellation.IsCancellationRequested).IsTrue();
        await Assert.That(lease.Principal.Scopes).Contains("events:read");
        await Assert.That(lease.ExpiresAtUtc).IsEqualTo(Now.AddDays(1));
    }

    [Test]
    public async Task DisposedCustodyCannotBeReusedAfterSourceReplacement()
    {
        var client = Client();
        using var source = new CredentialOptions(new() { Clients = [client] });
        var reader = SourceReader(source.Monitor, new CustodyClock());
        var lease = await reader.AcquireAsync(client.KeyId, default);
        await Assert.That(lease).IsNotNull();
        await lease!.DisposeAsync();
        source.Replace(new());
        await Assert.That(lease.ValidityCancellation.IsCancellationRequested).IsTrue();
        await Assert.That(await lease.RevalidateAsync(default)).IsFalse();
        await lease.DisposeAsync();
    }

    [Test]
    public async Task SourceWithoutExpiryUsesOnlyItsOriginalInvalidationInsteadOfAnInventedLifetime()
    {
        var client = Client();
        client.ExpiresAtUtc = null;
        using var source = new CredentialOptions(new() { Clients = [client] });
        var clock = new CustodyClock();
        var reader = SourceReader(source.Monitor, clock);
        await using var lease = await reader.AcquireAsync(client.KeyId, default);
        await Assert.That(lease).IsNotNull();
        await Assert.That(lease!.ExpiresAtUtc).IsNull();
        clock.Advance(TimeSpan.FromDays(100));
        await Assert.That(await lease.RevalidateAsync(default)).IsTrue();
        source.Replace(new());
        await Assert.That(lease.ValidityCancellation.IsCancellationRequested).IsTrue();
        await Assert.That(await lease.RevalidateAsync(default)).IsFalse();
    }

    [Test]
    public async Task InvalidationDuringRevalidationCannotReturnCurrentEvenWithTheSameSourceObject()
    {
        var client = Client();
        using var source = new CredentialOptions(new() { Clients = [client] });
        var monitor = new RevalidationBoundary(source);
        var reader = SourceReader(monitor, new CustodyClock());
        await using var lease = await reader.AcquireAsync(client.KeyId, default);
        await Assert.That(lease).IsNotNull();
        monitor.InvalidateNextRead = true;
        await Assert.That(await lease!.RevalidateAsync(default)).IsFalse();
        await Assert.That(lease.ValidityCancellation.IsCancellationRequested).IsTrue();
    }

    private sealed class CredentialOptions : IOptionsFactory<ApiKeyAuthenticationOptions>,
        IOptionsChangeTokenSource<ApiKeyAuthenticationOptions>, IDisposable
    {
        private ApiKeyAuthenticationOptions current;
        private CancellationTokenSource changes = new();
        public OptionsMonitor<ApiKeyAuthenticationOptions> Monitor { get; }
        public string Name => ApiAuthenticationSchemeNames.ApiKey;
        public CredentialOptions(ApiKeyAuthenticationOptions current)
        {
            this.current = current;
            Monitor = new(this, [this], new OptionsCache<ApiKeyAuthenticationOptions>());
        }
        public ApiKeyAuthenticationOptions Create(string name) =>
            name == Name ? current : throw new InvalidOperationException("Only the named credential source is valid.");
        public IChangeToken GetChangeToken() => new CancellationChangeToken(changes.Token);
        public void Replace(ApiKeyAuthenticationOptions replacement)
        {
            var previous = changes;
            changes = new();
            current = replacement;
            try { previous.Cancel(); }
            finally { previous.Dispose(); }
        }
        public void Dispose()
        {
            Monitor.Dispose();
            changes.Dispose();
        }
    }

    private sealed class SubscriptionBoundary(CredentialOptions source, bool beforeSubscription,
        ApiKeyAuthenticationOptions replacement) : IOptionsMonitor<ApiKeyAuthenticationOptions>
    {
        public ApiKeyAuthenticationOptions CurrentValue => throw new InvalidOperationException("Named source required.");
        public ApiKeyAuthenticationOptions Get(string? name) => source.Monitor.Get(name);
        public IDisposable? OnChange(Action<ApiKeyAuthenticationOptions, string?> listener)
        {
            if (beforeSubscription) source.Replace(replacement);
            var registration = source.Monitor.OnChange(listener);
            if (!beforeSubscription) source.Replace(replacement);
            return registration;
        }
    }

    private sealed class RevalidationBoundary(CredentialOptions source) : IOptionsMonitor<ApiKeyAuthenticationOptions>
    {
        public bool InvalidateNextRead { get; set; }
        public ApiKeyAuthenticationOptions CurrentValue => throw new InvalidOperationException("Named source required.");
        public ApiKeyAuthenticationOptions Get(string? name)
        {
            var result = source.Monitor.Get(name);
            if (InvalidateNextRead)
            {
                InvalidateNextRead = false;
                source.Replace(result);
            }
            return result;
        }
        public IDisposable? OnChange(Action<ApiKeyAuthenticationOptions, string?> listener) =>
            source.Monitor.OnChange(listener);
    }

    private sealed class CustodyClock : TimeProvider
    {
        private DateTimeOffset utc = Now;
        private long timestamp;
        private readonly List<CustodyTimer> timers = [];
        public override DateTimeOffset GetUtcNow() => utc;
        public override long GetTimestamp() => timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new CustodyTimer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan elapsed, TimeSpan? wallAdvance = null)
        {
            timestamp += elapsed.Ticks;
            utc += wallAdvance ?? elapsed;
            foreach (var timer in timers.ToArray()) timer.Fire();
        }
        private sealed class CustodyTimer(CustodyClock clock, TimerCallback callback, object? state) : ITimer
        {
            private long? due;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan || dueTime.TotalMilliseconds > uint.MaxValue - 1d)
                    throw new NotSupportedException("The native single-shot timer range must be preserved.");
                due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.timestamp + dueTime.Ticks;
                return true;
            }
            public void Fire()
            {
                if (due is not { } at || clock.timestamp < at) return;
                due = null;
                callback(state);
            }
            public void Dispose()
            {
                due = null;
                clock.timers.Remove(this);
            }
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
