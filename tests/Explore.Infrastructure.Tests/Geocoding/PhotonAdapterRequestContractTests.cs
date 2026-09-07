using System.Net;

namespace Explore.Infrastructure.Tests.Geocoding;

[NotInParallel("PhotonContract")]
public sealed class PhotonAdapterRequestContractTests
{
    [Test]
    public async Task Options_DefaultProvider_IsNone()
    {
        object options = PhotonAdapterContractHost.CreateDefaultOptions();
        object? provider = options.GetType().GetProperty("Provider")?.GetValue(options);

        await Assert.That(provider?.ToString()).IsEqualTo("None");
    }

    [Test]
    public async Task SearchAsync_WhenProviderIsNone_IsHealthyAndMakesNoHttpRequest()
    {
        var handler = new PhotonScriptedHttpHandler();
        using var host = CreateHost(handler, photonEnabled: false);

        PhotonSearchOutcome outcome = await host.SearchAsync();

        await Assert.That(outcome.Suggestions).IsEmpty();
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task SearchAsync_WhenPhotonEnabled_SendsBoundedApiRequestWithExplicitLocaleAndCountries()
    {
        var handler = new PhotonScriptedHttpHandler(
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.OK));
        Task firstCall = handler.ExpectCall(1);
        using var host = CreateHost(handler, photonEnabled: true, maximumResults: 3);

        Task<PhotonSearchOutcome> search = host.SearchAsync("Rue de l'Événement 30", limit: 17);
        await firstCall.WaitAsync(TimeSpan.FromSeconds(2));
        await search;

        Uri uri = handler.RequestUris.Single();
        IReadOnlyList<KeyValuePair<string, string>> parameters = ParseQuery(uri);
        await Assert.That(uri.AbsolutePath).IsEqualTo("/api");
        await Assert.That(parameters.Single(item => item.Key == "q").Value)
            .IsEqualTo("Rue de l'Événement 30");
        await Assert.That(parameters.Single(item => item.Key == "limit").Value).IsEqualTo("3");
        await Assert.That(parameters.Single(item => item.Key == "lang").Value).IsEqualTo("fr");

        KeyValuePair<string, string>[] countryParameters = parameters
            .Where(item => item.Key is not "q" and not "limit" and not "lang")
            .ToArray();
        await Assert.That(countryParameters.Select(item => item.Key).Distinct().Count()).IsEqualTo(1);
        await Assert.That(countryParameters.Select(item => item.Value.ToUpperInvariant()).ToArray())
            .IsEquivalentTo(["BE", "NL"]);
        await Assert.That(parameters.Count).IsEqualTo(5);
    }

    private static PhotonAdapterContractHost CreateHost(
        PhotonScriptedHttpHandler handler,
        bool photonEnabled,
        int maximumResults = 3)
    {
        return PhotonAdapterContractHost.Create(
            handler,
            new PhotonManualTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero)),
            new PhotonObservabilityCapture(),
            photonEnabled,
            maximumResults);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ParseQuery(Uri uri)
    {
        return uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(component => component.Split('=', 2))
            .Select(parts => new KeyValuePair<string, string>(
                Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
                Uri.UnescapeDataString((parts.Length == 2 ? parts[1] : string.Empty).Replace('+', ' '))))
            .ToArray();
    }
}
