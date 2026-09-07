using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.ControlPlane;

public sealed record ConfigurationManifestDownloadResult(
    bool Started,
    HalResourceOfControlPlaneOverviewDto Capabilities);
