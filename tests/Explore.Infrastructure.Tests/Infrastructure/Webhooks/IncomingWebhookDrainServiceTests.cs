using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class IncomingWebhookDrainServiceTests
{
    [Test]
    public async Task ProcessBatch_EnforcesConfiguredConcurrencyAndReportsEveryOutcome()
    {
        var claims = Enumerable.Range(0, 5)
            .Select(_ => new IncomingWebhookClaim(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                1,
                1))
            .ToArray();
        var repository = Substitute.For<IIncomingWebhookQueueRepository>();
        repository.ClaimMessagesAsync(
                Arg.Any<IncomingWebhookClaimRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IncomingWebhookClaim>>(claims));
        var executor = new ConcurrencyTrackingExecutor(expectedConcurrency: 2);

        var services = new ServiceCollection();
        services.AddSingleton(repository);
        await using var provider = services.BuildServiceProvider();
        var settings = new IncomingWebhookProcessingSettings
        {
            BatchSize = claims.Length,
            MaxConcurrentItems = 2,
            LeaseSeconds = 30
        };
        var service = new IncomingWebhookDrainService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            executor,
            Options.Create(settings),
            TimeProvider.System,
            NullLogger<IncomingWebhookDrainService>.Instance);

        var result = await service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.ClaimedCount).IsEqualTo(claims.Length);
        await Assert.That(result.CompletedCount).IsEqualTo(claims.Length);
        await Assert.That(result.LeaseLostCount).IsEqualTo(0);
        await Assert.That(result.AuthorizationDeniedCount).IsEqualTo(0);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(executor.MaximumConcurrency).IsEqualTo(2);
    }

    [Test]
    public async Task EffectProcessBatch_EnforcesConfiguredConcurrencyAndReportsEveryOutcome()
    {
        var claims = Enumerable.Range(0, 5)
            .Select(_ => new IncomingWebhookEffectClaim(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                1,
                1))
            .ToArray();
        var repository = Substitute.For<IIncomingWebhookQueueRepository>();
        repository.ClaimEffectsAsync(
                Arg.Any<IncomingWebhookEffectClaimRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IncomingWebhookEffectClaim>>(claims));
        var executor = new EffectConcurrencyTrackingExecutor(expectedConcurrency: 2);

        var services = new ServiceCollection();
        services.AddSingleton(repository);
        await using var provider = services.BuildServiceProvider();
        var service = new IncomingWebhookEffectDrainService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            executor,
            Options.Create(new IncomingWebhookProcessingSettings
            {
                BatchSize = claims.Length,
                MaxConcurrentItems = 2,
                LeaseSeconds = 30
            }),
            TimeProvider.System,
            CreateMetrics(),
            NullLogger<IncomingWebhookEffectDrainService>.Instance);

        var result = await service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.ClaimedCount).IsEqualTo(claims.Length);
        await Assert.That(result.CompletedCount).IsEqualTo(claims.Length);
        await Assert.That(result.LeaseLostCount).IsEqualTo(0);
        await Assert.That(result.AuthorizationDeniedCount).IsEqualTo(0);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(executor.MaximumConcurrency).IsEqualTo(2);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RenewalLossCancelsInFlightExecutionWithTheExactPersistedClaim(bool effects, bool renewalThrows)
    {
        var claim = new IncomingWebhookClaim(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 7, 3);
        var effect = new IncomingWebhookEffectClaim(
            claim.IncomingWebhookMessageId, claim.TenantId, claim.LeaseToken, claim.ProcessingFence, claim.ProcessingGeneration);
        var renewed = new TaskCompletionSource<IncomingWebhookClaim>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = Substitute.For<IIncomingWebhookQueueRepository>();
        repository.ClaimMessagesAsync(Arg.Any<IncomingWebhookClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IncomingWebhookClaim>>([claim]));
        repository.ClaimEffectsAsync(Arg.Any<IncomingWebhookEffectClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IncomingWebhookEffectClaim>>([effect]));
        repository.TryRenewMessageAsync(Arg.Any<IncomingWebhookClaim>(), Arg.Any<DateTime>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                renewed.TrySetResult(call.Arg<IncomingWebhookClaim>()
                    ?? throw new InvalidOperationException("Renewal requires an incoming claim."));
                return renewalThrows
                    ? Task.FromException<bool>(new InvalidOperationException("Renewal unavailable."))
                    : Task.FromResult(false);
            });
        repository.TryRenewEffectAsync(Arg.Any<IncomingWebhookEffectClaim>(), Arg.Any<DateTime>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var observed = call.Arg<IncomingWebhookEffectClaim>()
                    ?? throw new InvalidOperationException("Renewal requires an incoming effect claim.");
                renewed.TrySetResult(new IncomingWebhookClaim(observed.EffectOutboxId, observed.TenantId,
                    observed.LeaseToken, observed.ProcessingFence, observed.ProcessingGeneration));
                return renewalThrows
                    ? Task.FromException<bool>(new InvalidOperationException("Renewal unavailable."))
                    : Task.FromResult(false);
            });
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        await using var provider = services.BuildServiceProvider();
        var executor = new CancellationObservingExecutor();
        using var clock = new RenewalClock();
        var settings = Options.Create(new IncomingWebhookProcessingSettings
        {
            BatchSize = 1,
            LeaseSeconds = 30,
            MaxConcurrentItems = 1
        });
        using var metrics = CreateMetrics();
        Task<IncomingWebhookDrainResult> batch = effects
            ? new IncomingWebhookEffectDrainService(
                provider.GetRequiredService<IServiceScopeFactory>(), executor, settings, clock, metrics,
                NullLogger<IncomingWebhookEffectDrainService>.Instance).ProcessBatchAsync(CancellationToken.None)
            : new IncomingWebhookDrainService(
                provider.GetRequiredService<IServiceScopeFactory>(), executor, settings, clock,
                NullLogger<IncomingWebhookDrainService>.Instance).ProcessBatchAsync(CancellationToken.None);
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Tick();
        await Assert.That(await renewed.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(claim);
        await executor.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var result = await batch.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result.ClaimedCount).IsEqualTo(1);
        await Assert.That(result.LeaseLostCount).IsEqualTo(1);
        await Assert.That(result.CompletedCount).IsEqualTo(0);
        await Assert.That(result.FailedCount).IsEqualTo(0);
    }

    private sealed class CancellationObservingExecutor : IIncomingWebhookClaimExecutor, IIncomingWebhookEffectClaimExecutor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(IncomingWebhookClaim claim, CancellationToken token) =>
            ExecuteAsync(token);

        public Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(IncomingWebhookEffectClaim claim, CancellationToken token) =>
            ExecuteAsync(token);

        private async Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(CancellationToken token)
        {
            Started.TrySetResult();
            try
            {
                await _completion.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                return IncomingWebhookClaimExecutionResult.Completed();
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class RenewalClock : TimeProvider, IDisposable
    {
        private DateTimeOffset _now = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);
        private RenewalTimer? _timer;
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            _timer = new RenewalTimer(callback, state);

        public void Tick()
        {
            _now = _now.AddSeconds(10);
            (_timer ?? throw new InvalidOperationException("Renewal timer was not registered.")).Tick();
        }

        public void Dispose() => _timer?.Dispose();

        private sealed class RenewalTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Tick()
            {
                if (!_disposed)
                    callback(state);
            }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ConcurrencyTrackingExecutor(int expectedConcurrency) : IIncomingWebhookClaimExecutor
    {
        private readonly TaskCompletionSource _expectedConcurrencyReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maximumConcurrency;

        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public async Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(
            IncomingWebhookClaim claim,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            if (active == expectedConcurrency)
            {
                _expectedConcurrencyReached.TrySetResult();
            }

            try
            {
                await _expectedConcurrencyReached.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                return IncomingWebhookClaimExecutionResult.Completed();
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            var current = Volatile.Read(ref _maximumConcurrency);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(ref _maximumConcurrency, candidate, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class EffectConcurrencyTrackingExecutor(int expectedConcurrency)
        : IIncomingWebhookEffectClaimExecutor
    {
        private readonly TaskCompletionSource _expectedConcurrencyReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maximumConcurrency;

        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public async Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(
            IncomingWebhookEffectClaim claim,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            if (active == expectedConcurrency)
            {
                _expectedConcurrencyReached.TrySetResult();
            }

            try
            {
                await _expectedConcurrencyReached.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                return IncomingWebhookClaimExecutionResult.Completed();
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            var current = Volatile.Read(ref _maximumConcurrency);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(ref _maximumConcurrency, candidate, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }
}
