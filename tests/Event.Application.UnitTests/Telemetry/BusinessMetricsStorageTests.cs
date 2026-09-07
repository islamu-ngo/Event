using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using Explore.Domain;
using NSubstitute;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("BusinessMetricsMeter")]
public sealed class BusinessMetricsStorageTests
{
    [Test]
    public async Task RecordStorageUploadSessionRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordStorageUploadSession(StorageProviders.Local, "create", "succeeded");

        var measurement = await metricsCapture.SingleAsync("explore.storage.upload_sessions");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo(StorageProviders.Local);
        await Assert.That(measurement.Tags["operation"]?.ToString()).IsEqualTo("create");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("succeeded");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("none");
    }

    [Test]
    public async Task StorageByteHistogramsRecordExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordStorageUploadBytes(4096, StorageProviders.S3Compatible, "succeeded");
        metrics.RecordStorageQuotaBytes(4096, StorageProviders.S3Compatible, "reserve", "succeeded");
        metrics.RecordStorageReadBytes(4096, StorageProviders.S3Compatible, "succeeded", StorageObjectVisibilities.PublicImage);

        var uploadBytes = await metricsCapture.SingleAsync("explore.storage.upload_bytes");
        var quotaBytes = await metricsCapture.SingleAsync("explore.storage.quota_bytes");
        var readBytes = await metricsCapture.SingleAsync("explore.storage.read_bytes");

        await Assert.That(uploadBytes.Value).IsEqualTo(4096);
        await Assert.That(uploadBytes.Tags["provider"]?.ToString()).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(uploadBytes.Tags["outcome"]?.ToString()).IsEqualTo("succeeded");
        await Assert.That(uploadBytes.Tags["failure_category"]?.ToString()).IsEqualTo("none");

        await Assert.That(quotaBytes.Value).IsEqualTo(4096);
        await Assert.That(quotaBytes.Tags["operation"]?.ToString()).IsEqualTo("reserve");

        await Assert.That(readBytes.Value).IsEqualTo(4096);
        await Assert.That(readBytes.Tags["visibility"]?.ToString()).IsEqualTo(StorageObjectVisibilities.PublicImage);
    }

    [Test]
    public async Task RecordStorageProviderTestRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordStorageProviderTest(StorageProviders.S3Compatible, "failed", "s3_not_configured");

        var measurement = await metricsCapture.SingleAsync("explore.storage.provider_tests");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("failed");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("s3_not_configured");
    }

    [Test]
    public async Task StorageMetricsDoNotEmitSensitiveOrHighCardinalityTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();
        var rawIdentifier = Guid.NewGuid().ToString();

        metrics.RecordStorageRead(
            $"https://storage.example/{rawIdentifier}",
            "failed",
            $"tenants/{rawIdentifier}/object.png",
            $"private-owner-{rawIdentifier}");
        metrics.RecordStorageDelete($"bucket-{rawIdentifier}", "failed", $"access-key-{rawIdentifier}");
        metrics.RecordStorageProviderTest(StorageProviders.Local, "succeeded");

        var measurements = await metricsCapture.AllAsync(expectedCount: 3);
        var tagKeys = measurements.SelectMany(measurement => measurement.Tags.Keys).ToArray();
        var tagValues = string.Join(" ", measurements.SelectMany(measurement => measurement.Tags.Values.Select(value => value?.ToString())));

        await Assert.That(tagKeys).DoesNotContain("tenant_id");
        await Assert.That(tagKeys).DoesNotContain("storage_object_id");
        await Assert.That(tagKeys).DoesNotContain("upload_session_id");
        await Assert.That(tagKeys).DoesNotContain("object_key");
        await Assert.That(tagKeys).DoesNotContain("path");
        await Assert.That(tagKeys).DoesNotContain("filename");
        await Assert.That(tagKeys).DoesNotContain("endpoint");
        await Assert.That(tagKeys).DoesNotContain("bucket");
        await Assert.That(tagKeys).DoesNotContain("access_key");
        await Assert.That(tagKeys).DoesNotContain("secret");
        await Assert.That(tagKeys).DoesNotContain("error");
        await Assert.That(tagKeys).DoesNotContain("exception");

        await Assert.That(tagValues).DoesNotContain(rawIdentifier);
        await Assert.That(tagValues).DoesNotContain("storage.example");
        await Assert.That(tagValues).DoesNotContain("object.png");
        await Assert.That(tagValues).DoesNotContain("access-key");
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    private sealed class MetricsCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly Lock _measurementsLock = new();
        private readonly List<Measurement> _measurements = [];

        public MetricsCapture()
        {
            _listener = new MeterListener();
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BusinessMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                lock (_measurementsLock)
                {
                    _measurements.Add(new Measurement(
                        instrument.Name,
                        measurement,
                        tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
                }
            });

            _listener.Start();
        }

        public async Task<Measurement> SingleAsync(string instrumentName)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var matches = Snapshot()
                    .Where(measurement => measurement.InstrumentName == instrumentName)
                    .ToList();

                if (matches.Count > 0)
                {
                    return matches.Single();
                }

                await Task.Delay(10);
            }

            return Snapshot()
                .Where(measurement => measurement.InstrumentName == instrumentName)
                .Single();
        }

        public async Task<IReadOnlyList<Measurement>> AllAsync(int expectedCount)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var snapshot = Snapshot();
                if (snapshot.Length >= expectedCount)
                {
                    return snapshot;
                }

                await Task.Delay(10);
            }

            return Snapshot();
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private Measurement[] Snapshot()
        {
            lock (_measurementsLock)
            {
                return [.. _measurements];
            }
        }
    }

    private sealed record Measurement(string InstrumentName, long Value, IReadOnlyDictionary<string, object?> Tags);
}
