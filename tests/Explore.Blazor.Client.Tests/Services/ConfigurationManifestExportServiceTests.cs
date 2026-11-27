using Explore.Blazor.Client.Contracts.InstanceAdmin;
using Explore.Blazor.Client.Contracts.Services.InstanceAdmin;
using Explore.Blazor.Client.Routing.InstanceAdmin;
using Explore.Blazor.Client.Services.InstanceAdmin;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class ConfigurationManifestExportServiceTests
{
    [Test]
    [Arguments(ConfigurationManifestExportView.Overrides, ConfigurationManifestExportScope.Instance,
        "export-configuration-overrides")]
    [Arguments(ConfigurationManifestExportView.Portable, ConfigurationManifestExportScope.Instance,
        "export-configuration-portable")]
    [Arguments(ConfigurationManifestExportView.Overrides, ConfigurationManifestExportScope.InstanceAndTenants,
        "export-configuration-overrides-with-tenants")]
    [Arguments(ConfigurationManifestExportView.Portable, ConfigurationManifestExportScope.InstanceAndTenants,
        "export-configuration-portable-with-tenants")]
    public async Task Download_WithHalCapabilityUsesFixedBffRouteAndIgnoresRawApiHref(
        ConfigurationManifestExportView view, ConfigurationManifestExportScope scope, string relation)
    {
        var overviewService = Substitute.For<IInstanceOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(Overview(
                relation,
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
            view, scope);

        await Assert.That(result.Started).IsTrue();
        await browser.Received(1).DownloadFileFromUrlAsync(
            $"{ConfigurationManifestExportRoutes.BffExport}?view={view}&scope={scope}",
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
        var overviewService = Substitute.For<IInstanceOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(Overview(
                InstanceAdminLinkRelations.ExportConfigurationPortable,
                "/api/admin/instance/configuration-manifest/export?view=Portable"));
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
            ConfigurationManifestExportView.Portable, ConfigurationManifestExportScope.Instance);

        await Assert.That(result.Started).IsTrue();
        await browser.Received(1).DownloadFileFromUrlAsync(
            $"/community{ConfigurationManifestExportRoutes.BffExport}?view=Portable&scope=Instance",
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Download_WithoutMatchingHalCapabilityDoesNotInvokeBrowser(bool combinedScope)
    {
        var overviewService = Substitute.For<IInstanceOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(combinedScope ? Overview(InstanceAdminLinkRelations.ExportConfigurationOverrides) : Overview());
        var browser = Substitute.For<IBrowserActionInterop>();
        var service = new ConfigurationManifestExportService(
            overviewService,
            browser,
            new TestNavigationManager("https://event.example/"));

        ConfigurationManifestDownloadResult result = await service.DownloadAsync(
            ConfigurationManifestExportView.Overrides, combinedScope
                ? ConfigurationManifestExportScope.InstanceAndTenants : ConfigurationManifestExportScope.Instance);

        await Assert.That(result.Started).IsFalse();
        await browser.DidNotReceive().DownloadFileFromUrlAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments("POST")]
    [Arguments("")]
    public async Task Download_RequiresAdvertisedGetMethod(string method)
    {
        var capabilities = Overview(InstanceAdminLinkRelations.ExportConfigurationOverrides);
        capabilities._links[InstanceAdminLinkRelations.ExportConfigurationOverrides].Method = method;
        var overviewService = Substitute.For<IInstanceOverviewService>();
        overviewService.GetOverviewAsync(Arg.Any<CancellationToken>()).Returns(capabilities);
        var browser = Substitute.For<IBrowserActionInterop>();
        browser.DownloadFileFromUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var service = new ConfigurationManifestExportService(
            overviewService, browser, new TestNavigationManager("https://event.example/"));

        var result = await service.DownloadAsync(
            ConfigurationManifestExportView.Overrides, ConfigurationManifestExportScope.Instance);

        await Assert.That(result.Started).IsFalse();
        await browser.DidNotReceive().DownloadFileFromUrlAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static HalResourceOfInstanceOverviewDto Overview(
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
