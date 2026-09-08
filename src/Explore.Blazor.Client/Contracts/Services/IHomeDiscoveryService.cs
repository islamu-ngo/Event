using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IHomeDiscoveryService
{
    Task<HomeDiscoveryDto?> LoadAsync(
        Guid? urlAreaId,
        string? urlMode,
        CancellationToken cancellationToken = default);

    Task<HomeDiscoveryDto?> SelectAreaAsync(
        Guid areaId,
        CancellationToken cancellationToken = default);

    Task<HomeDiscoveryDto?> SelectOnlineAsync(
        Guid? preservedAreaId,
        CancellationToken cancellationToken = default);

    PublicDiscoveryAreaDto? FindClosestArea(
        IEnumerable<PublicDiscoveryAreaDto> areas,
        double latitude,
        double longitude);
}
