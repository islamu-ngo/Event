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
using TUnit.Core;

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
        var repository = Substitute.For<IIncomingWebhookMessageRepository>();
        repository.ClaimDueAsync(
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
        var repository = Substitute.For<IIncomingWebhookEffectOutboxRepository>();
        repository.ClaimDueAsync(
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
                await Task.Delay(10, cancellationToken);
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
                await Task.Delay(10, cancellationToken);
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
