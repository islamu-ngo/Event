using System.Text.Json;
using Explore.Application.Features.EventReporting.Models;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.EventReporting.Models;

public sealed class EventReportProviderContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task EventReportProviderEnvelope_WhenSerialized_ContainsSafeMetadataOnly()
    {
        var envelope = new EventReportProviderEnvelope(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "spam",
            "safety",
            "submitted",
            "open",
            "normal",
            new DateTime(2026, 7, 2, 10, 15, 0, DateTimeKind.Utc),
            null,
            "report-sync-key",
            "correlation-123");

        var json = JsonSerializer.Serialize(envelope, JsonOptions);

        await Assert.That(json).Contains("reasonCode");
        await Assert.That(json).Contains("queueCode");
        await Assert.That(json).Contains("idempotencyKey");
        await Assert.That(json).Contains("caseConcurrencyStamp");
        await Assert.That(json).Contains("evidenceMode");
        await Assert.That(json).DoesNotContain("reporterIpHash");
        await Assert.That(json).DoesNotContain("reporterUserAgentHash");
        await Assert.That(json).DoesNotContain("textBody");
        await Assert.That(json).DoesNotContain("rawProviderPayload");
        await Assert.That(json).DoesNotContain("unsafe reporter text");
    }

    [Test]
    public async Task ResultFactories_RecordProviderDisabledRetryAndSignalState()
    {
        var signal = new EventSafetySignalEnvelope(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            EventReportSignalProvider.Osprey,
            "policy_match",
            "event.spam",
            0.91m,
            EventReportSignalVerdict.LikelyViolation,
            EventReportRecommendedAction.LightModerate,
            "Policy matched public event metadata.",
            "external-signal-1",
            "correlation-123",
            DateTime.UtcNow);

        var success = EventReportProviderSyncResult.Success(
            "provider-case-1",
            "external-signal-1",
            "https://provider.example/cases/provider-case-1",
            [signal]);
        var disabled = EventReportProviderSyncResult.Disabled("mode is LocalOnly");
        var retryableFailure = EventReportProviderSyncResult.Failure("timeout", isTransient: true);

        await Assert.That(success.Succeeded).IsTrue();
        await Assert.That(success.Signals).Count().IsEqualTo(1);
        await Assert.That(success.ProviderCaseId).IsEqualTo("provider-case-1");
        await Assert.That(disabled.ProviderDisabled).IsTrue();
        await Assert.That(disabled.Error!.Category).IsEqualTo("provider_disabled");
        await Assert.That(retryableFailure.IsRetryable).IsTrue();
        await Assert.That(retryableFailure.Error!.IsTransient).IsTrue();
    }

    [Test]
    public async Task DecisionExecutionResult_RepresentsIdempotentAlreadyCompleteOutcome()
    {
        var moderationRecordId = Guid.CreateVersion7();

        var result = ReportDecisionExecutionResult.AlreadyComplete(moderationRecordId);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.AlreadyExecuted).IsTrue();
        await Assert.That(result.IsRetryable).IsFalse();
        await Assert.That(result.ModerationRecordId).IsEqualTo(moderationRecordId);
        await Assert.That(result.Error).IsNull();
    }
}
