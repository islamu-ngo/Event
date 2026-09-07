using System.Globalization;
using System.Net;

namespace Explore.Infrastructure.Tests.Geocoding;

[NotInParallel("PhotonContract")]
public sealed class PhotonAdapterTelemetryContractTests
{
    private static readonly Guid TenantCanary = Guid.Parse("019d2f3a-3d0d-7cc2-a75b-2c1e2fd3b2d1");
    private static readonly Guid UserCanary = Guid.Parse("019d2f3a-3d0d-7f84-a79a-099da3f69c1f");

    [Test]
    public async Task SearchAsync_LogsMetricsAndActivitiesExposeOnlyBoundedOperationalDimensions()
    {
        const string query = "QUERY-PII-CANARY";
        const string address = "ADDRESS-PII-CANARY";
        const string postcode = "POSTCODE-PII-CANARY";
        const string providerRecordId = "PROVIDER-ID-PII-CANARY";
        const double latitude = 50.850312345;
        const double longitude = 4.351712345;
        string json = PhotonGeoJsonFixtures.Feature(
            "DISPLAY-PII-CANARY",
            address,
            "30",
            postcode,
            longitude,
            latitude,
            providerRecordId);
        var handler = new PhotonScriptedHttpHandler(
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.OK, json));
        var observability = new PhotonObservabilityCapture();
        Task firstCall = handler.ExpectCall(1);
        using var host = PhotonAdapterContractHost.Create(
            handler,
            new PhotonManualTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero)),
            observability);

        Task<PhotonSearchOutcome> operation = host.SearchAsync(query, tenantId: TenantCanary, userId: UserCanary);
        await firstCall.WaitAsync(TimeSpan.FromSeconds(2));
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(observability.Logs).IsNotEmpty();
        await Assert.That(observability.Measurements).IsNotEmpty();
        await Assert.That(observability.Activities).IsNotEmpty();

        string observable = observability.ObservableText();
        string[] forbiddenValues =
        [
            query,
            "https://photon.operator.test",
            address,
            postcode,
            providerRecordId,
            "DISPLAY-PII-CANARY",
            latitude.ToString(CultureInfo.InvariantCulture),
            longitude.ToString(CultureInfo.InvariantCulture),
            TenantCanary.ToString("D"),
            UserCanary.ToString("D"),
            "dataset-canary-v1"
        ];
        foreach (string value in forbiddenValues)
        {
            await Assert.That(observable).DoesNotContain(value);
        }

        string[] forbiddenKeyFragments =
        [
            "query", "uri", "url", "address", "postcode", "coordinate", "latitude", "longitude",
            "record_id", "tenant", "user", "organization", "token", "exception", "error"
        ];
        string[] emittedKeys = observability.Logs
            .SelectMany(log => log.Properties.Select(item => item.Key))
            .Where(key => key != "{OriginalFormat}")
            .Concat(observability.Measurements.SelectMany(item => item.Tags.Select(tag => tag.Key)))
            .Concat(observability.Activities.SelectMany(item => item.Tags.Select(tag => tag.Key)))
            .ToArray();
        foreach (string key in emittedKeys)
        {
            foreach (string fragment in forbiddenKeyFragments)
            {
                await Assert.That(key.ToLowerInvariant()).DoesNotContain(fragment);
            }

            await Assert.That(IsAllowedDimension(key)).IsTrue();
        }
    }

    private static bool IsAllowedDimension(string key)
    {
        string normalized = key.Replace('.', '_').Replace('-', '_').ToLowerInvariant();
        return normalized.EndsWith("provider", StringComparison.Ordinal)
            || normalized.EndsWith("outcome", StringComparison.Ordinal)
            || normalized.EndsWith("retry_count", StringComparison.Ordinal)
            || normalized.EndsWith("latency_bucket", StringComparison.Ordinal);
    }
}
