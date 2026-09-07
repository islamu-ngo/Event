// ABOUTME: Unit-style tests for the API-hosted EmailDispatch fallback processor cycle.
// ABOUTME: Proves hosted-service triggering delegates to the shared drain boundary with configured worker settings.

using Event.Api.IntegrationTests.Fixtures;
using Explore.API.BackgroundServices;
using Explore.Application.Contracts.Services;
using Explore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[Category(TestCategories.Email)]
public sealed class EmailDispatchProcessorTests
{
    [Test]
    public async Task RunOnceAsync_WhenHostedFallbackRuns_RecoversBeforeDrainingThroughSharedService()
    {
        var settings = new EmailDispatchProcessorSettings
        {
            Enabled = true,
            Mode = EmailDispatchProcessorMode.HostedService,
            PollingIntervalSeconds = 3,
            BatchSize = 17,
            ConsumerId = "hosted-service-test"
        };
        var recorder = new RecordingDrainCalls();
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<EmailDispatchProcessorSettings>>(Options.Create(settings));
        services.AddSingleton(recorder);
        services.AddScoped<IEmailDispatchDrainService, RecordingDrainService>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var runner = new EmailDispatchHostedDrainRunner(provider);
        using var cancellation = new CancellationTokenSource();

        await runner.RunOnceAsync(cancellation.Token);

        await Assert.That(recorder.Calls).IsEquivalentTo(
            ["recover", "batch"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(recorder.BatchSizes).IsEquivalentTo(
            [17, 17], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(recorder.PollingIntervals).IsEquivalentTo(
            [3, 3], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(recorder.ConsumerIds).IsEquivalentTo(
            ["hosted-service-test", "hosted-service-test"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(recorder.CancellationTokens.All(token => token == cancellation.Token)).IsTrue();
    }

    private sealed class RecordingDrainCalls
    {
        private readonly List<string> _calls = [];
        private readonly List<int> _batchSizes = [];
        private readonly List<int> _pollingIntervals = [];
        private readonly List<string> _consumerIds = [];
        private readonly List<CancellationToken> _cancellationTokens = [];

        public IReadOnlyList<string> Calls => _calls;
        public IReadOnlyList<int> BatchSizes => _batchSizes;
        public IReadOnlyList<int> PollingIntervals => _pollingIntervals;
        public IReadOnlyList<string> ConsumerIds => _consumerIds;
        public IReadOnlyList<CancellationToken> CancellationTokens => _cancellationTokens;

        public void Record(string call, EmailDispatchProcessorSettings settings, CancellationToken cancellationToken)
        {
            _calls.Add(call);
            _batchSizes.Add(settings.BatchSize);
            _pollingIntervals.Add(settings.PollingIntervalSeconds);
            _consumerIds.Add(settings.ConsumerId);
            _cancellationTokens.Add(cancellationToken);
        }
    }

    private sealed class RecordingDrainService(
        IOptions<EmailDispatchProcessorSettings> settings,
        RecordingDrainCalls recorder) : IEmailDispatchDrainService
    {
        public Task<EmailDispatchDrainResult> ProcessBatchAsync(CancellationToken cancellationToken)
        {
            recorder.Record("batch", settings.Value, cancellationToken);
            return Task.FromResult(new EmailDispatchDrainResult
            {
                PendingCount = settings.Value.BatchSize,
                ProcessedCount = settings.Value.BatchSize
            });
        }

        public Task<EmailDispatchRecoveryResult> RecoverStaleProcessingAsync(CancellationToken cancellationToken)
        {
            recorder.Record("recover", settings.Value, cancellationToken);
            return Task.FromResult(new EmailDispatchRecoveryResult(
                RecoveredCount: 0,
                ProcessingStartedBefore: DateTime.UtcNow));
        }

        public Task<EmailDispatchSingleDrainResult> ProcessSingleAsync(
            Guid tenantId,
            Guid publishEventId,
            string consumerId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Hosted fallback batch runner must not call pointer dispatch.");
        }
    }
}
