using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.InstanceAdmin;

public interface IConfigurationManifestExportService
{
    Task<HalResourceOfInstanceOverviewDto> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default);

    Task<ConfigurationManifestDownloadResult> DownloadAsync(
        ConfigurationManifestExportView view,
        CancellationToken cancellationToken = default);
}
