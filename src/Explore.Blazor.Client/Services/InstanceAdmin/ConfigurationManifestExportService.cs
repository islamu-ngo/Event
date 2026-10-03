using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.InstanceAdmin;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services.InstanceAdmin;
using Explore.Blazor.Client.Routing.InstanceAdmin;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Services.InstanceAdmin;

public sealed class ConfigurationManifestExportService(
    IInstanceOverviewService overviewService,
    IBrowserActionInterop browserActions,
    NavigationManager navigation)
    : IConfigurationManifestExportService
{
    public Task<HalResourceOfInstanceOverviewDto> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default) =>
        overviewService.GetOverviewAsync(cancellationToken);

    public async Task<ConfigurationManifestDownloadResult> DownloadAsync(
        ConfigurationManifestExportView view,
        CancellationToken cancellationToken = default)
    {
        HalResourceOfInstanceOverviewDto capabilities =
            await overviewService.GetOverviewAsync(cancellationToken);
        string relation = RelationFor(view);

        if (!InstanceAdminHal.HasLink(capabilities._links, relation))
        {
            return new ConfigurationManifestDownloadResult(false, capabilities);
        }

        bool started = await browserActions.DownloadFileFromUrlAsync(
            BuildSameOriginBffPath(view),
            cancellationToken);

        return new ConfigurationManifestDownloadResult(started, capabilities);
    }

    public static string RelationFor(ConfigurationManifestExportView view) =>
        view switch
        {
            ConfigurationManifestExportView.Overrides =>
                InstanceAdminLinkRelations.ExportConfigurationOverrides,
            ConfigurationManifestExportView.Portable =>
                InstanceAdminLinkRelations.ExportConfigurationPortable,
            _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Unsupported export view.")
        };

    private string BuildSameOriginBffPath(ConfigurationManifestExportView view)
    {
        var baseUri = new Uri(navigation.BaseUri, UriKind.Absolute);
        string pathBase = baseUri.AbsolutePath.TrimEnd('/');
        return $"{pathBase}{ConfigurationManifestExportRoutes.BffExport}?view={view}";
    }
}
