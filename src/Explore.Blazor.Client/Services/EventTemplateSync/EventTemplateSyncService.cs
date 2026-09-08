using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.EventTemplateSync;

public sealed class EventTemplateSyncService : IEventTemplateSyncService
{
    private readonly IEventTemplateSyncClient _api;

    public EventTemplateSyncService(IEventTemplateSyncClient api)
    {
        _api = api;
    }

    public Task<HalResourceOfTemplateDiffDto> GetDiffAsync(Guid eventId, int templateVersion, CancellationToken cancellationToken = default) =>
        _api.GetEventTemplateSyncDiffAsync(eventId, templateVersion, cancellationToken: cancellationToken);

    public Task<TemplateSyncOutcomeDto> ApplySyncAsync(Guid eventId, EventTemplateSyncApplyRequest request, CancellationToken cancellationToken = default) =>
        _api.ApplyEventTemplateSyncAsync(eventId, request, cancellationToken: cancellationToken);

    public Task<PaginatedResultOfEventTemplateSyncHistoryItemDto> GetHistoryAsync(Guid eventId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
        _api.GetEventTemplateSyncHistoryAsync(eventId, page, pageSize, cancellationToken: cancellationToken);
}
