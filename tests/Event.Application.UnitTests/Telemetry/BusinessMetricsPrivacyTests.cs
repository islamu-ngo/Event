using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace ApplicationUnitTests.Telemetry;

public sealed class BusinessMetricsPrivacyTests
{
    [Test]
    public async Task OperationalDimensionsRejectPrivateTextWithoutLosingCounts()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        var meter = factory.Create(new MeterOptions(BusinessMetrics.MeterName));
        var measurements = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, subscription) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
                subscription.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => measurements.Add(tags.ToArray()));
        listener.Start();
        using var metrics = new BusinessMetrics(factory);
        var privateText = $"{Guid.NewGuid():N}@example.invalid\r\nforged-event";

        metrics.RecordRegistrationProviderManagementAction(privateText, privateText);
        metrics.RecordEventRoleAssignmentChanged(privateText, privateText, privateText);
        metrics.RecordCustomPropertyPurgeDecision(null, privateText, privateText, privateText);
        metrics.RecordIdempotencyCleanupRun(privateText, privateText);
        metrics.RecordIdempotencyCleanupRows(1, privateText, privateText);
        metrics.RecordAiRetentionCleanupRun(privateText, privateText);
        metrics.RecordAiRetentionCleanupRows(1, privateText, privateText);
        metrics.RecordAiProviderHealthCheck(privateText, privateText, privateText);
        metrics.RecordCustomPropertyPurgeDecision(null, "custom_property_definition", "blocked_after_recheck", "audit_log");
        metrics.RecordIdempotencyCleanupRows(1, "scrub", "scrubbed");
        metrics.RecordAiProviderHealthCheck("fake", "healthy", "healthy_fake");

        await Assert.That(measurements.Count).IsEqualTo(11);
        foreach (var tags in measurements.Take(8))
            foreach (var tag in tags)
                await Assert.That(tag.Value?.ToString() ?? string.Empty).DoesNotContain(privateText);
        await Assert.That(measurements[8].Single(tag => tag.Key == "outcome").Value).IsEqualTo("blocked_after_recheck");
        await Assert.That(measurements[8].Single(tag => tag.Key == "blocker_category").Value).IsEqualTo("audit_log");
        await Assert.That(measurements[9].Single(tag => tag.Key == "outcome").Value).IsEqualTo("scrubbed");
        await Assert.That(measurements[10].Single(tag => tag.Key == "reason").Value).IsEqualTo("healthy_fake");
    }

    [Test]
    public async Task ApiKeyDimensionsKeepDeclaredCategoriesAndRejectPrivateText()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        var meter = factory.Create(new MeterOptions(BusinessMetrics.MeterName));
        var measurements = new List<Dictionary<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, subscription) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
                subscription.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            measurements.Add(tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
        listener.Start();
        using var metrics = new BusinessMetrics(factory);
        var privateText = $"{Guid.NewGuid():N}@example.invalid\r\nforged-event";

        metrics.RecordExternalApiKeyAuthentication(privateText, ownerType: privateText);
        metrics.RecordExternalApiKeyThrottle(privateText, ownerType: privateText);
        metrics.RecordExternalApiKeyAuthentication("tenant_required", ownerType: "InstanceAdmin");
        metrics.RecordExternalApiKeyThrottle("admission_scanner_check_in", ownerType: "User");

        await Assert.That(measurements.Count).IsEqualTo(4);
        await Assert.That(measurements[0]["outcome"]).IsEqualTo("unknown");
        await Assert.That(measurements[0]["owner_type"]).IsEqualTo("unknown");
        await Assert.That(measurements[1]["policy"]).IsEqualTo("unknown");
        await Assert.That(measurements[1]["owner_type"]).IsEqualTo("unknown");
        await Assert.That(measurements[2]["outcome"]).IsEqualTo("tenant_required");
        await Assert.That(measurements[2]["owner_type"]).IsEqualTo("InstanceAdmin");
        await Assert.That(measurements[3]["policy"]).IsEqualTo("admission_scanner_check_in");
        await Assert.That(measurements[3]["owner_type"]).IsEqualTo("User");
        foreach (var fields in measurements)
            foreach (var field in fields.Values)
                await Assert.That(field?.ToString() ?? string.Empty).DoesNotContain(privateText);
    }

    [Test]
    public async Task TenantIdentitiesDoNotReachBusinessMeasurements()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        var meter = factory.Create(new MeterOptions(BusinessMetrics.MeterName));
        var measurements = new List<(string Name, long Count, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, subscription) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
                subscription.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, count, tags, _) =>
            measurements.Add((instrument.Name, count, tags.ToArray())));
        listener.Start();
        using var metrics = new BusinessMetrics(factory);
        var identity = $"{Guid.NewGuid():N}@example.invalid\r\nforged-event";

        metrics.RecordEventPublished(identity);
        metrics.RecordEventModerationAction(identity, "heavy_redacted", "pending_storage_deletion",
            "storage_deletion_pending", irreversible: true);
        metrics.RecordEventReportSubmission(identity, "submitted");
        metrics.RecordEventReportWorkflowAction(identity, "execute_report_decision", "failed");
        metrics.RecordEventReportProviderSync(identity, "coop", "retryable_failure");
        metrics.RecordRegistrationCreated(identity);
        metrics.RecordOrganizationCreated(identity);
        metrics.RecordExternalApiKeyCreated(identity, "InstanceAdmin");
        metrics.RecordExternalApiKeyRevoked(identity, "InstanceAdmin");
        metrics.RecordExternalApiKeyAuthentication("invalid", identity, "InstanceAdmin");
        metrics.RecordExternalApiKeyThrottle("minute", identity, "InstanceAdmin");
        metrics.RecordExternalApiKeyPolicyUpdated(identity, "InstanceAdmin");
        metrics.RecordExternalApiKeyRotated(identity, "InstanceAdmin");
        metrics.RecordCustomPropertyPurgeDecision(identity, "event", "denied", "authority_unavailable");

        await Assert.That(measurements.Count).IsEqualTo(14);
        await Assert.That(measurements.Sum(measurement => measurement.Count)).IsEqualTo(14);
        foreach (var measurement in measurements)
        {
            await Assert.That(measurement.Tags.Select(tag => tag.Key)).DoesNotContain("tenant_id");
            foreach (var tag in measurement.Tags)
                await Assert.That(tag.Value?.ToString() ?? string.Empty).DoesNotContain(identity);
        }

        var moderation = measurements.Single(measurement =>
            measurement.Name == "explore.events.moderation_actions");
        await Assert.That(moderation.Tags.Single(tag => tag.Key == "outcome").Value)
            .IsEqualTo("pending_storage_deletion");
        await Assert.That(moderation.Tags.Single(tag => tag.Key == "failure_category").Value)
            .IsEqualTo("storage_deletion_pending");
    }
}
