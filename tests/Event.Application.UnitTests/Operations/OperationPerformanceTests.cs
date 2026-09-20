using System.Collections.Concurrent;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Event.Application.UnitTests.Operations;

public sealed class OperationPerformanceTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task OverlappingCallsKeepInvocationLocalTimingAndOnlyLogSafeMetadata(int shape)
    {
        var services = CreateServices(out var clock, out var log);
        using var logOwner = log;
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ControlledHandler>();
        var first = Invoke(scope.ServiceProvider, shape, 0);
        await handler.Started[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(600);
        var second = Invoke(scope.ServiceProvider, shape, 1);
        await handler.Started[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(200);
        handler.Completion[0].SetResult(10);
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(400);
        handler.Completion[1].SetResult(20);
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        var warnings = log.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        await Assert.That(warnings.Select(entry => (long)entry.Properties.Single(pair => pair.Key == "ElapsedMilliseconds").Value!).ToArray())
            .IsEquivalentTo(new long[] { 800, 600 });
        await Assert.That(warnings.SelectMany(entry => entry.Properties).Select(pair => pair.Key).Distinct().Order().ToArray())
            .IsEquivalentTo(new[] { "RequestType", "ElapsedMilliseconds", "{OriginalFormat}" });
        await Assert.That(warnings.Any(entry => entry.Message.Contains("private operation payload", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CancellationAndFailurePropagateWithoutSuccessfulTimingLogs(int shape)
    {
        var services = CreateServices(out var clock, out var log);
        using var logOwner = log;
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ControlledHandler>();
        using var source = new CancellationTokenSource();
        var cancelled = Invoke(scope.ServiceProvider, shape, 0, source.Token);
        await handler.Started[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(1000);
        source.Cancel();
        await Assert.That(async () => await cancelled.WaitAsync(TimeSpan.FromSeconds(5))).Throws<OperationCanceledException>();
        var failed = Invoke(scope.ServiceProvider, shape, 1);
        await handler.Started[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
        var expected = new InvalidOperationException("controlled business failure");
        handler.Completion[1].SetException(expected);
        var observed = await Assert.That(async () => await failed.WaitAsync(TimeSpan.FromSeconds(5))).Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(expected, observed)).IsTrue();
        await Assert.That(log.Entries.Where(entry => entry.Level == LogLevel.Warning)).IsEmpty();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task DenialOccursOutsideTiming(int shape)
    {
        var services = OperationAuthorizationTests.CreateServices(new OperationAuthorizationTests.Policy(
            AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local)));
        var clock = new ManualClock();
        services.AddSingleton<TimeProvider>(clock);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await Assert.That(() => OperationAuthorizationTests.Invoke(scope.ServiceProvider, shape))
            .Throws<Explore.Application.Exceptions.AuthorizationException>();
        await Assert.That(clock.TimestampReads).IsEqualTo(0);
    }

    private static IServiceCollection CreateServices(out ManualClock clock, out CaptureProvider log)
    {
        var services = OperationCompositionTests.Services(typeof(TimedWrite), typeof(TimedResult), typeof(TimedQuery), typeof(ControlledHandler));
        clock = new ManualClock();
        log = new CaptureProvider();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ILoggerProvider>(log);
        services.AddSingleton<IAuthorizationProvider>(new OperationAuthorizationTests.Policy(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)));
        return services;
    }
    private static Task Invoke(IServiceProvider provider, int shape, int slot, CancellationToken cancellationToken = default) => shape switch
    {
        0 => provider.GetRequiredService<ICommandHandler<TimedWrite>>().ExecuteAsync(new(slot), cancellationToken),
        1 => provider.GetRequiredService<ICommandHandler<TimedResult, int>>().ExecuteAsync(new(slot), cancellationToken),
        2 => provider.GetRequiredService<IQueryHandler<TimedQuery, int>>().QueryAsync(new(slot), cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };
    public sealed record TimedWrite(int Slot, string Text = "private operation payload") : ICommand;
    public sealed record TimedResult(int Slot, string Text = "private operation payload") : ICommand<int>;
    public sealed record TimedQuery(int Slot, string Text = "private operation payload") : IQuery<int>;
    public sealed class ControlledHandler : ICommandHandler<TimedWrite>, ICommandHandler<TimedResult, int>, IQueryHandler<TimedQuery, int>
    {
        public TaskCompletionSource[] Started { get; } = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        public TaskCompletionSource<int>[] Completion { get; } = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        private Task<int> Run(int slot, CancellationToken cancellationToken)
        {
            Started[slot].SetResult();
            return Completion[slot].Task.WaitAsync(cancellationToken);
        }
        public Task ExecuteAsync(TimedWrite command, CancellationToken cancellationToken) => Run(command.Slot, cancellationToken);
        public Task<int> ExecuteAsync(TimedResult command, CancellationToken cancellationToken) => Run(command.Slot, cancellationToken);
        public Task<int> QueryAsync(TimedQuery query, CancellationToken cancellationToken) => Run(query.Slot, cancellationToken);
    }
    public sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public int TimestampReads { get; private set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp()
        {
            TimestampReads++;
            return _timestamp;
        }
        public void Advance(long milliseconds) => _timestamp += milliseconds;
    }
    public sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(Entries);
        public void Dispose() { }
        private sealed class Logger(ConcurrentQueue<Entry> entries) : ILogger
        {
            public bool IsEnabled(LogLevel logLevel) => true;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new Entry(logLevel, formatter(state, exception), ((IEnumerable<KeyValuePair<string, object?>>)state!).ToArray()));
        }
    }
    public sealed record Entry(LogLevel Level, string Message, KeyValuePair<string, object?>[] Properties);
}
