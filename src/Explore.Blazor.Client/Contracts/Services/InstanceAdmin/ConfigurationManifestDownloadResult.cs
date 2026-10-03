using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.InstanceAdmin;

public sealed record ConfigurationManifestDownloadResult(
    bool Started,
    HalResourceOfInstanceOverviewDto Capabilities);
