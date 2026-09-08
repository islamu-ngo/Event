using System.Net;

namespace Explore.Infrastructure.Tests.Geocoding;

[NotInParallel("PhotonContract")]
public sealed class PhotonAdapterGeoJsonContractTests
{
    [Test]
    public async Task SearchAsync_MapsGeoJsonLongitudeLatitudeIntoProviderNeutralLatitudeLongitude()
    {
        string json = PhotonGeoJsonFixtures.Feature(
            "Community Hall",
            "Rue Provider",
            "30",
            "1000",
            longitude: 4.3517,
            latitude: 50.8503,
            recordId: "12345");
        using var host = CreateHost(json);

        PhotonSuggestionView suggestion = (await host.SearchAsync()).Suggestions.Single();

        await Assert.That(suggestion.DisplayName).IsEqualTo("Community Hall");
        await Assert.That(suggestion.Address).Contains("Rue Provider");
        await Assert.That(suggestion.Address).Contains("30");
        await Assert.That(suggestion.Postcode).IsEqualTo("1000");
        await Assert.That(suggestion.Latitude).IsEqualTo(50.8503);
        await Assert.That(suggestion.Longitude).IsEqualTo(4.3517);
        await Assert.That(suggestion.Provider).IsEqualTo("Photon");
        await Assert.That(suggestion.ProviderRecordId).Contains("12345");
        await Assert.That(suggestion.Attribution).Contains("OpenStreetMap");
        await Assert.That(suggestion.DatasetVersion)
            .IsEqualTo("dataset-canary-v1");
    }

    [Test]
    public async Task SearchAsync_DiscardsMalformedAndOutOfBoundsFeaturesButRetainsValidFeature()
    {
        string json = PhotonGeoJsonFixtures.Features(
            PhotonGeoJsonFixtures.FeatureBody("Longitude invalid", 181, 50, "1"),
            PhotonGeoJsonFixtures.FeatureBody("Latitude invalid", 4, 91, "2"),
            "{\"type\":\"Feature\",\"geometry\":{\"type\":\"LineString\",\"coordinates\":[]},\"properties\":{\"name\":\"Wrong geometry\"}}",
            PhotonGeoJsonFixtures.FeatureBody("Valid", 4.4, 50.9, "3"));
        using var host = CreateHost(json, maximumResults: 20);

        IReadOnlyList<PhotonSuggestionView> suggestions = (await host.SearchAsync()).Suggestions;

        await Assert.That(suggestions.Count).IsEqualTo(1);
        await Assert.That(suggestions.Single().DisplayName).IsEqualTo("Valid");
    }

    [Test]
    public async Task SearchAsync_MalformedGeoJson_ReturnsNoProviderSuggestions()
    {
        using var host = CreateHost("{not-json");

        PhotonSearchOutcome outcome = await host.SearchAsync();

        await Assert.That(outcome.Suggestions).IsEmpty();
    }

    [Test]
    public async Task SearchAsync_ResponseAboveConfiguredByteBound_ReturnsNoProviderSuggestions()
    {
        string oversized = "{\"type\":\"FeatureCollection\",\"features\":[],\"padding\":\""
            + new string('x', 2_048)
            + "\"}";
        using var host = CreateHost(oversized, maximumResponseBytes: 1_024);

        PhotonSearchOutcome outcome = await host.SearchAsync();

        await Assert.That(outcome.Suggestions).IsEmpty();
    }

    [Test]
    public async Task SearchAsync_ProviderReturnsMoreFeaturesThanAllowed_TruncatesAtMaximumResultCount()
    {
        string json = PhotonGeoJsonFixtures.Features(
            PhotonGeoJsonFixtures.FeatureBody("One", 4.1, 50.1, "1"),
            PhotonGeoJsonFixtures.FeatureBody("Two", 4.2, 50.2, "2"),
            PhotonGeoJsonFixtures.FeatureBody("Three", 4.3, 50.3, "3"));
        using var host = CreateHost(json, maximumResults: 2);

        IReadOnlyList<PhotonSuggestionView> suggestions = (await host.SearchAsync(limit: 20)).Suggestions;

        await Assert.That(suggestions.Select(item => item.DisplayName).ToArray())
            .IsEquivalentTo(["One", "Two"]);
    }

    private static PhotonAdapterContractHost CreateHost(
        string response,
        int maximumResults = 3,
        int maximumResponseBytes = 65_536)
    {
        return PhotonAdapterContractHost.Create(
            new PhotonScriptedHttpHandler(
                PhotonScriptedHttpHandler.Respond(HttpStatusCode.OK, response)),
            new PhotonManualTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero)),
            new PhotonObservabilityCapture(),
            maximumResults: maximumResults,
            maximumResponseBytes: maximumResponseBytes);
    }
}
