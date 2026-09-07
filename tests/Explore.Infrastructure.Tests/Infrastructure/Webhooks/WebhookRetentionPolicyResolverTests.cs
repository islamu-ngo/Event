using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookRetentionPolicyResolverTests
{
    [Test]
    public async Task Resolve_SnapshotsIndependentHorizonsAndOutboundContractOverride()
    {
        var settings = new WebhookRetentionSettings
        {
            InboundPayloadRetentionDays = 20,
            OutboundPayloadRetentionDays = 21,
            ProcessingAttemptRetentionDays = 35,
            DeadLetterEvidenceRetentionDays = 100,
            ProviderPublicationRetentionDays = 95,
            OperationalLogRetentionDays = 40,
            AdministrativeAuditRetentionDays = 400,
            ReplayWindowDays = 18
        };
        var sourceOccurredAt = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        var materializedAt = sourceOccurredAt.AddHours(2);
        var resolver = new WebhookRetentionPolicyResolver(
            new StaticOptionsMonitor<WebhookRetentionSettings>(settings));

        var policy = resolver.Resolve(sourceOccurredAt, materializedAt, outboundPayloadRetentionDays: 28);

        await Assert.That(policy.PolicyVersion)
            .IsEqualTo("webhook-retention-v1:i20:o28:a35:d100:p95:l40:u400:r18");
        await Assert.That(policy.InboundPayloadRetentionUntil).IsEqualTo(materializedAt.AddDays(20));
        await Assert.That(policy.OutboundPayloadRetentionUntil).IsEqualTo(sourceOccurredAt.AddDays(28));
        await Assert.That(policy.ProcessingAttemptRetentionUntil).IsEqualTo(materializedAt.AddDays(35));
        await Assert.That(policy.DeadLetterEvidenceRetentionUntil).IsEqualTo(materializedAt.AddDays(100));
        await Assert.That(policy.ProviderPublicationRetentionUntil).IsEqualTo(materializedAt.AddDays(95));
        await Assert.That(policy.OperationalLogRetentionUntil).IsEqualTo(materializedAt.AddDays(40));
        await Assert.That(policy.AdministrativeAuditRetentionUntil).IsEqualTo(materializedAt.AddDays(400));
        await Assert.That(policy.ReplayWindowUntil).IsEqualTo(materializedAt.AddDays(18));
    }

    [Test]
    public async Task Validator_RejectsUnsafeOrderingAndOutOfRangeWorkerLimits()
    {
        var settings = new WebhookRetentionSettings
        {
            BatchSize = 0,
            InboundPayloadRetentionDays = 7,
            ReplayWindowDays = 14,
            ProcessingAttemptRetentionDays = 90,
            DeadLetterEvidenceRetentionDays = 30
        };

        var result = new WebhookRetentionSettingsValidator().Validate(null, settings);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains("WebhookRetention:BatchSize must be between 1 and 10000.");
        await Assert.That(result.Failures)
            .Contains("WebhookRetention:InboundPayloadRetentionDays cannot be shorter than ReplayWindowDays.");
        await Assert.That(result.Failures)
            .Contains("WebhookRetention:DeadLetterEvidenceRetentionDays cannot be shorter than ProcessingAttemptRetentionDays.");
    }

    [Test]
    public async Task Resolve_WhenMaterializationPrecedesSourceOccurrence_RejectsPolicy()
    {
        var sourceOccurredAt = DateTimeOffset.UtcNow;
        var resolver = new WebhookRetentionPolicyResolver(
            new StaticOptionsMonitor<WebhookRetentionSettings>(new WebhookRetentionSettings()));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(
            resolver.Resolve(sourceOccurredAt, sourceOccurredAt.AddTicks(-1))));
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
