using System.Diagnostics.Metrics;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("BusinessMetricsMeter")]
public sealed class WebhookSensitiveSurfaceAbsenceTests
{
    [Test]
    public async Task PublicContractsAndMetrics_ExposeOnlyBoundedSafeMetadata()
    {
        var attemptEntityProperties = PropertyNames<WebhookDeliveryAttempt>();
        var attemptDtoProperties = PropertyNames<WebhookDeliveryAttemptDto>();
        var endpointDtoProperties = PropertyNames<WebhookEndpointDto>();
        var consumerDtoProperties = PropertyNames<WebhookConsumerDto>();

        await Assert.That(attemptEntityProperties).DoesNotContain("ResponseBodyPreview");
        await Assert.That(attemptDtoProperties).DoesNotContain("ResponseBodyPreview");
        await Assert.That(attemptDtoProperties).DoesNotContain("EndpointUrl");
        await Assert.That(endpointDtoProperties).DoesNotContain("Url");
        await Assert.That(endpointDtoProperties).Contains(nameof(WebhookEndpointDto.DestinationHost));
        await Assert.That(consumerDtoProperties).DoesNotContain("CanOpenProviderPortal");
        await Assert.That(typeof(WebhookMessage).Assembly.GetType("Explore.Domain.WebhookProviderTargetSnapshot")).IsNull();

        using var listener = new MeterListener();
        var measurements = new List<Measurement>();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == BusinessMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, tags.ToArray())));
        listener.Start();

        var meterFactory = Substitute.For<IMeterFactory>();
        using var meter = new Meter(BusinessMetrics.MeterName);
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(meter);
        using var metrics = new BusinessMetrics(meterFactory);
        metrics.RecordWebhookDeliveryFailure("event.published", "abandoned", "http_non_success");
        metrics.RecordEventReportProviderCallback("svix", "failed", "webhook_signature_invalid");

        var webhookMeasurement = measurements.Single(item =>
            item.InstrumentName == "explore.webhooks.delivery_failure");
        var callbackMeasurement = measurements.Single(item =>
            item.InstrumentName == "explore.event_reports.provider_callbacks");

        await Assert.That(webhookMeasurement.Tags.Select(tag => tag.Key)).DoesNotContain("tenant_id");
        await Assert.That(callbackMeasurement.Tags.Select(tag => tag.Key)).DoesNotContain("tenant_id");
        await Assert.That(webhookMeasurement.Value).IsEqualTo(1);
        await Assert.That(callbackMeasurement.Value).IsEqualTo(1);
    }

    private static string[] PropertyNames<T>() =>
        typeof(T).GetProperties().Select(property => property.Name).ToArray();

    private sealed record Measurement(
        string InstrumentName,
        long Value,
        KeyValuePair<string, object?>[] Tags);
}
