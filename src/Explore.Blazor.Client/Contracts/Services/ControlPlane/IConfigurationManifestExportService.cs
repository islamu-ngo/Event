using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.ControlPlane;

public interface IConfigurationManifestExportService
{
    Task<HalResourceOfControlPlaneOverviewDto> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default);

    Task<ConfigurationManifestDownloadResult> DownloadAsync(
        ConfigurationManifestExportView view,
        CancellationToken cancellationToken = default);
}
