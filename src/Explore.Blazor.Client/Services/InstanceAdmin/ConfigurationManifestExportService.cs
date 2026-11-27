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
        ConfigurationManifestExportScope scope,
        CancellationToken cancellationToken = default)
    {
        string relation = InstanceAdminHal.ExportConfigurationRelation(view, scope);
        HalResourceOfInstanceOverviewDto capabilities =
            await overviewService.GetOverviewAsync(cancellationToken);

        if (capabilities._links?.TryGetValue(relation, out HalLink? link) != true
            || !string.Equals(link.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return new ConfigurationManifestDownloadResult(false, capabilities);
        }

        bool started = await browserActions.DownloadFileFromUrlAsync(
            BuildSameOriginBffPath(view, scope),
            cancellationToken);

        return new ConfigurationManifestDownloadResult(started, capabilities);
    }

    private string BuildSameOriginBffPath(
        ConfigurationManifestExportView view, ConfigurationManifestExportScope scope)
    {
        var baseUri = new Uri(navigation.BaseUri, UriKind.Absolute);
        string pathBase = baseUri.AbsolutePath.TrimEnd('/');
        return $"{pathBase}{ConfigurationManifestExportRoutes.BffExport}?view={view}&scope={scope}";
    }
}
