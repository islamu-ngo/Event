using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.EventSessionTemplateSync;

public sealed class EventSessionTemplateSyncService : IEventSessionTemplateSyncService
{
    private readonly IEventSessionTemplateSyncClient _api;

    public EventSessionTemplateSyncService(IEventSessionTemplateSyncClient api)
    {
        _api = api;
    }

    public Task<HalResourceOfTemplateDiffDto> GetDiffAsync(Guid sessionId, int templateVersion, CancellationToken cancellationToken = default) =>
        _api.GetEventSessionTemplateSyncDiffAsync(sessionId, templateVersion, cancellationToken: cancellationToken);

    public Task<TemplateSyncOutcomeDto> ApplySyncAsync(Guid sessionId, EventSessionTemplateSyncApplyRequest request, CancellationToken cancellationToken = default) =>
        _api.ApplyEventSessionTemplateSyncAsync(sessionId, request, cancellationToken: cancellationToken);

    public Task<PaginatedResultOfEventSessionTemplateSyncHistoryItemDto> GetHistoryAsync(Guid sessionId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
        _api.GetEventSessionTemplateSyncHistoryAsync(sessionId, page, pageSize, cancellationToken: cancellationToken);
}
