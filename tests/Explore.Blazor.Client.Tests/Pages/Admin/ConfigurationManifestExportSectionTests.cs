using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class ConfigurationManifestExportSectionTests : IDisposable
{
    private const string OverridesRelation = "export-configuration-overrides";
    private const string PortableRelation = "export-configuration-portable";
    private readonly BlazorTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_WithBothHalCapabilitiesShowsOnlyCanonicalDownloadActions()
    {
        var service = ExportService(Overview(OverridesRelation, PortableRelation));
        _ctx.Services.AddSingleton(service);

        var cut = _ctx.Render<ConfigurationManifestExportSection>();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Download configuration overrides']"));

        await Assert.That(cut.FindAll("button[aria-label='Download configuration overrides']")).HasSingleItem();
        await Assert.That(cut.FindAll("button[aria-label='Download portable configuration']")).HasSingleItem();
        await Assert.That(cut.Markup).Contains("whole instance");
        await Assert.That(cut.Markup).Contains("Secrets are omitted");
        await Assert.That(cut.Markup).Contains("not a backup");
        await Assert.That(cut.Markup).DoesNotContain("/api/control-plane/configuration-manifest/export");
    }

    [Test]
    public async Task Render_WithoutHalCapabilitiesRemovesEveryDownloadEntryPoint()
    {
        var service = ExportService(Overview());
        _ctx.Services.AddSingleton(service);

        var cut = _ctx.Render<ConfigurationManifestExportSection>();
        cut.WaitForAssertion(() => cut.Find("#configuration-manifest-export-heading"));

        await Assert.That(cut.FindAll("button[aria-label^='Download ']")).IsEmpty();
        await Assert.That(cut.Markup).Contains("Export is unavailable");
    }

    [Test]
    public async Task Download_RevalidatesCapabilityAndAnnouncesStartedAction()
    {
        var capabilities = Overview(OverridesRelation);
        var service = ExportService(capabilities);
        service.DownloadAsync(
                ConfigurationManifestExportView.Overrides,
                Arg.Any<CancellationToken>())
            .Returns(new ConfigurationManifestDownloadResult(true, capabilities));
        _ctx.Services.AddSingleton(service);
        var announcer = _ctx.Services.GetRequiredService<IAccessibilityAnnouncerService>();

        var cut = _ctx.Render<ConfigurationManifestExportSection>();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Download configuration overrides']"));
        cut.Find("button[aria-label='Download configuration overrides']").Click();
        cut.WaitForAssertion(() =>
            service.Received(1).DownloadAsync(
                ConfigurationManifestExportView.Overrides,
                Arg.Any<CancellationToken>()));

        await announcer.Received(1).AnnouncePoliteAsync(
            Arg.Is<string>(message => message.Contains("started", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public async Task Download_WhenCapabilityDisappearsRemovesActionsAndMovesFocusToSafeStatus()
    {
        var service = ExportService(Overview(PortableRelation));
        service.DownloadAsync(
                ConfigurationManifestExportView.Portable,
                Arg.Any<CancellationToken>())
            .Returns(new ConfigurationManifestDownloadResult(false, Overview()));
        _ctx.Services.AddSingleton(service);
        var focus = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();

        var cut = _ctx.Render<ConfigurationManifestExportSection>();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Download portable configuration']"));
        cut.Find("button[aria-label='Download portable configuration']").Click();
        cut.WaitForAssertion(() => cut.Find("#configuration-manifest-export-status"));

        await Assert.That(cut.FindAll("button[aria-label^='Download ']")).IsEmpty();
        await Assert.That(cut.Find("#configuration-manifest-export-status").GetAttribute("role"))
            .IsEqualTo("alert");
        await focus.Received(1).FocusByIdAsync("configuration-manifest-export-status");
    }

    private static IConfigurationManifestExportService ExportService(
        HalResourceOfControlPlaneOverviewDto capabilities)
    {
        var service = Substitute.For<IConfigurationManifestExportService>();
        service.GetCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(capabilities);
        return service;
    }

    private static HalResourceOfControlPlaneOverviewDto Overview(params string[] relations) =>
        new()
        {
            _links = relations.ToDictionary(
                relation => relation,
                relation => new HalLink
                {
                    Href = $"/api/control-plane/configuration-manifest/export?relation={relation}",
                    Method = "GET"
                },
                StringComparer.Ordinal)
        };
}
