using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public sealed class EventResourceService(
    IEventResourcesClient audience,
    IEventResourceManagementClient management,
    IEventResourceExportClient export) : IEventResourceService
{
    private static string Key() => Guid.CreateVersion7().ToString("D");

    public Task<EventResourceAudiencePageResource> AudienceAsync(Guid eventId, string? cursor = null, CancellationToken cancellationToken = default) =>
        audience.ListEventResourcesAsync(eventId, cursor: cursor, cancellationToken: cancellationToken);

    public Task<HalResourceOfEventResourceAudienceDetailDto> AudienceDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
        audience.GetEventResourceAudienceDetailAsync(id, cancellationToken: cancellationToken);

    public Task<EventResourceManagementCollectionDto> ManagementAsync(Guid eventId, int page = 1, CancellationToken cancellationToken = default) =>
        management.ListEventResourceManagementAsync(eventId, page: page, cancellationToken: cancellationToken);

    public Task<HalResourceOfEventResourceManagementDto> ManagementDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
        management.GetEventResourceManagementDetailAsync(id, cancellationToken: cancellationToken);

    public Task<EventResourceAuditPageDto> AuditAsync(Guid id, CancellationToken cancellationToken = default) =>
        management.GetEventResourceAuditAsync(id, cancellationToken: cancellationToken);

    public Task<EventResourceMetadataExportPageDto> ExportAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        export.ExportEventResourceMetadataAsync(eventId, cancellationToken: cancellationToken);

    public async Task CreateAsync(Guid eventId, CreateEventResourceRequestDto request, CancellationToken cancellationToken = default) =>
        await management.CreateEventResourceAsync(eventId, Key(), request, cancellationToken: cancellationToken);

    public async Task UpdateAsync(Guid id, UpdateEventResourceRequestDto request, CancellationToken cancellationToken = default) =>
        await management.UpdateEventResourceAsync(id, Key(), request, cancellationToken: cancellationToken);

    public async Task DestinationAsync(Guid id, EventResourceDestinationWriteDto request, CancellationToken cancellationToken = default) =>
        await management.SetEventResourceDestinationAsync(id, Key(), request, cancellationToken: cancellationToken);

    public async Task MutateAsync(Guid id, string relation, Guid version, CancellationToken cancellationToken = default)
    {
        var body = new EventResourceVersionRequestDto { ExpectedVersion = version };
        var key = Key();
        switch (relation)
        {
            case "publish": await management.PublishEventResourceAsync(id, key, body, cancellationToken: cancellationToken); break;
            case "unpublish": await management.UnpublishEventResourceAsync(id, key, body, cancellationToken: cancellationToken); break;
            case "archive": await management.ArchiveEventResourceAsync(id, key, body, cancellationToken: cancellationToken); break;
            case "delete": await management.DeleteEventResourceAsync(id, key, body, cancellationToken: cancellationToken); break;
            case "moderate": await management.ModerateEventResourceAsync(id, key, body, cancellationToken: cancellationToken); break;
            default: throw new ArgumentOutOfRangeException(nameof(relation));
        }
    }
}
