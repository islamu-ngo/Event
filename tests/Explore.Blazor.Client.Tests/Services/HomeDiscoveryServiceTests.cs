using System.Net;
using System.Text;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class HomeDiscoveryServiceTests
{
    private readonly IPublicExperienceClient apiClient = Substitute.For<IPublicExperienceClient>();
    private readonly IUserSettingsService settingsService = Substitute.For<IUserSettingsService>();

    [Test]
    public async Task LoadAsyncUsesUrlContextBeforeSavedPreferences()
    {
        var urlAreaId = Guid.NewGuid();
        var expected = Home(urlAreaId, HomeDiscoveryMode.Online);
        apiClient.GetHomeDiscoveryAsync(
                urlAreaId,
                "online",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = CreateService();

        var result = await service.LoadAsync(urlAreaId, "online");

        await Assert.That(result).IsSameReferenceAs(expected);
        await settingsService.DidNotReceive().GetSettingsAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await apiClient.Received(1).GetHomeDiscoveryAsync(
            urlAreaId,
            "online",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadAsyncUsesSavedContextWhenUrlDoesNotSupplyIt()
    {
        var savedAreaId = Guid.NewGuid();
        settingsService.GetSettingsAsync("home-discovery", Arg.Any<CancellationToken>())
            .Returns(new SettingGroupResponseDto
            {
                Settings =
                [
                    new EffectiveSettingDto { Key = "home_discovery.area_id", Value = savedAreaId.ToString() },
                    new EffectiveSettingDto { Key = "home_discovery.mode", Value = "area" }
                ]
            });
        var expected = Home(savedAreaId, HomeDiscoveryMode.Area);
        apiClient.GetHomeDiscoveryAsync(
                savedAreaId,
                "area",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = CreateService();

        var result = await service.LoadAsync(null, null);

        await Assert.That(result).IsSameReferenceAs(expected);
        await apiClient.Received(1).GetHomeDiscoveryAsync(
            savedAreaId,
            "area",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SelectAreaAsyncPersistsOnlyCoarseAreaContext()
    {
        var areaId = Guid.NewGuid();
        var expected = Home(areaId, HomeDiscoveryMode.Area);
        settingsService.UpdateSettingsBatchAsync(
                "home-discovery",
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });
        apiClient.GetHomeDiscoveryAsync(
                areaId,
                "area",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = CreateService();

        var result = await service.SelectAreaAsync(areaId);

        await Assert.That(result).IsSameReferenceAs(expected);
        await settingsService.Received(1).UpdateSettingsBatchAsync(
            "home-discovery",
            Arg.Is<IDictionary<string, string>>(values =>
                values != null &&
                values.Count == 2 &&
                values["home_discovery.area_id"] == areaId.ToString() &&
                values["home_discovery.mode"] == "area"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SelectOnlineAsyncPreservesAreaAndPersistsOnlyMode()
    {
        var areaId = Guid.NewGuid();
        settingsService.UpdateSettingsBatchAsync(
                "home-discovery",
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });
        apiClient.GetHomeDiscoveryAsync(
                areaId,
                "online",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Home(areaId, HomeDiscoveryMode.Online));
        var service = CreateService();

        await service.SelectOnlineAsync(areaId);

        await settingsService.Received(1).UpdateSettingsBatchAsync(
            "home-discovery",
            Arg.Is<IDictionary<string, string>>(values =>
                values != null &&
                values.Count == 1 && values["home_discovery.mode"] == "online"),
            Arg.Any<CancellationToken>());
        await apiClient.Received(1).GetHomeDiscoveryAsync(
            areaId,
            "online",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindClosestAreaReducesTransientOriginToConfiguredArea()
    {
        var brussels = new PublicDiscoveryAreaDto
        {
            Id = Guid.NewGuid(),
            DisplayName = "Brussels",
            CentroidLatitude = 50.85,
            CentroidLongitude = 4.35
        };
        var antwerp = new PublicDiscoveryAreaDto
        {
            Id = Guid.NewGuid(),
            DisplayName = "Antwerp",
            CentroidLatitude = 51.22,
            CentroidLongitude = 4.40
        };
        var unavailable = new PublicDiscoveryAreaDto { Id = Guid.NewGuid(), DisplayName = "No centroid" };
        var service = CreateService();

        var result = service.FindClosestArea([antwerp, unavailable, brussels], 50.8466, 4.3528);

        await Assert.That(result).IsSameReferenceAs(brussels);
    }

    [Test]
    public async Task LoadAsyncReturnsNullWithoutLeakingRequestDetailsWhenApiFails()
    {
        var areaId = Guid.NewGuid();
        apiClient.GetHomeDiscoveryAsync(
                areaId,
                "area",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<HomeDiscoveryDto>>(_ => throw new HttpRequestException("network unavailable"));
        var service = CreateService();

        var result = await service.LoadAsync(areaId, "area");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GeneratedClientDeserializesStringEnumDictionaryValues()
    {
        const string responseBody = """
            {
              "schemaVersion": 1,
              "context": {
                "mode": "All",
                "selectedAreaDisplayName": "All events",
                "availableAreas": []
              },
              "hero": [],
              "upcomingInArea": [],
              "mostViewedInArea": [],
              "mostViewedOnline": [],
              "curatedSections": [],
              "recentlyAdded": [],
              "sectionStatuses": {
                "hero": "Available"
              },
              "generatedAtUtc": "2026-07-16T10:00:00Z"
            }
            """;
        using var httpClient = new HttpClient(new StaticResponseHandler(responseBody))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var client = new PublicExperienceClient(httpClient);

        var result = await client.GetHomeDiscoveryAsync();

        await Assert.That(result.SectionStatuses).IsNotNull();
        await Assert.That(result.SectionStatuses!["hero"]).IsEqualTo(HomeDiscoverySectionStatus.Available);
        await Assert.That(result.Context?.Mode).IsEqualTo(HomeDiscoveryMode.All);
    }

    private HomeDiscoveryService CreateService() => new(
        apiClient,
        settingsService,
        Substitute.For<ILogger<HomeDiscoveryService>>());

    private static HomeDiscoveryDto Home(Guid areaId, HomeDiscoveryMode mode) => new()
    {
        Context = new HomeDiscoveryContextDto
        {
            SelectedAreaId = areaId,
            Mode = mode
        }
    };

    private sealed class StaticResponseHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }
}
