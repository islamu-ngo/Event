using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Routing.ControlPlane;
using Explore.Blazor.Client.Services.ControlPlane;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class ConfigurationManifestExportServiceTests
{
    [Test]
    public async Task Download_WithHalCapabilityUsesFixedBffRouteAndIgnoresRawApiHref()
    {
        var overviewService = Substitute.For<IControlPlaneOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(Overview(
                ControlPlaneLinkRelations.ExportConfigurationOverrides,
                "https://attacker.example/export?access_token=raw-secret"));
        var browser = Substitute.For<IBrowserActionInterop>();
        browser.DownloadFileFromUrlAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var service = new ConfigurationManifestExportService(
            overviewService,
            browser,
            new TestNavigationManager("https://event.example/"));

        ConfigurationManifestDownloadResult result = await service.DownloadAsync(
            ConfigurationManifestExportView.Overrides);

        await Assert.That(result.Started).IsTrue();
        await browser.Received(1).DownloadFileFromUrlAsync(
            $"{ConfigurationManifestExportRoutes.BffExport}?view=Overrides",
            Arg.Any<CancellationToken>());
        await browser.DidNotReceive().DownloadFileFromUrlAsync(
            Arg.Is<string>(url =>
                url.Contains("attacker", StringComparison.Ordinal)
                || url.Contains("raw-secret", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Download_PreservesConfiguredApplicationPathBase()
    {
        var overviewService = Substitute.For<IControlPlaneOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(Overview(
                ControlPlaneLinkRelations.ExportConfigurationPortable,
                "/api/control-plane/configuration-manifest/export?view=Portable"));
        var browser = Substitute.For<IBrowserActionInterop>();
        browser.DownloadFileFromUrlAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var service = new ConfigurationManifestExportService(
            overviewService,
            browser,
            new TestNavigationManager("https://event.example/community/"));

        ConfigurationManifestDownloadResult result = await service.DownloadAsync(
            ConfigurationManifestExportView.Portable);

        await Assert.That(result.Started).IsTrue();
        await browser.Received(1).DownloadFileFromUrlAsync(
            $"/community{ConfigurationManifestExportRoutes.BffExport}?view=Portable",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Download_WithoutMatchingHalCapabilityDoesNotInvokeBrowser()
    {
        var overviewService = Substitute.For<IControlPlaneOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(Overview());
        var browser = Substitute.For<IBrowserActionInterop>();
        var service = new ConfigurationManifestExportService(
            overviewService,
            browser,
            new TestNavigationManager("https://event.example/"));

        ConfigurationManifestDownloadResult result = await service.DownloadAsync(
            ConfigurationManifestExportView.Overrides);

        await Assert.That(result.Started).IsFalse();
        await browser.DidNotReceive().DownloadFileFromUrlAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static HalResourceOfControlPlaneOverviewDto Overview(
        string? relation = null,
        string? href = null) =>
        new()
        {
            _links = relation is null
                ? new Dictionary<string, HalLink>()
                : new Dictionary<string, HalLink>
                {
                    [relation] = new HalLink
                    {
                        Href = href,
                        Method = "GET"
                    }
                }
        };

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri)
        {
            Initialize(baseUri, baseUri);
        }

        protected override void NavigateToCore(
            string uri,
            NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
