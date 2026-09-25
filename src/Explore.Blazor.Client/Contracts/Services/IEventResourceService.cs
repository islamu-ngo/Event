using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IEventResourceService
{
    Task<EventResourceAudiencePageResource> AudienceAsync(Guid eventId, string? cursor = null, CancellationToken cancellationToken = default);
    Task<HalResourceOfEventResourceAudienceDetailDto> AudienceDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EventResourceManagementCollectionDto> ManagementAsync(Guid eventId, int page = 1, CancellationToken cancellationToken = default);
    Task<HalResourceOfEventResourceManagementDto> ManagementDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EventResourceAuditPageDto> AuditAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EventResourceMetadataExportPageDto> ExportAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task CreateAsync(Guid eventId, CreateEventResourceRequestDto request, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid id, UpdateEventResourceRequestDto request, CancellationToken cancellationToken = default);
    Task DestinationAsync(Guid id, EventResourceDestinationWriteDto request, CancellationToken cancellationToken = default);
    Task MutateAsync(Guid id, string relation, Guid version, CancellationToken cancellationToken = default);
}
