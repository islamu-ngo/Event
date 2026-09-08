using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.EventSessionTemplateSync;

public interface IEventSessionTemplateSyncService
{
    Task<HalResourceOfTemplateDiffDto> GetDiffAsync(Guid sessionId, int templateVersion, CancellationToken cancellationToken = default);
    Task<TemplateSyncOutcomeDto> ApplySyncAsync(Guid sessionId, EventSessionTemplateSyncApplyRequest request, CancellationToken cancellationToken = default);
    Task<PaginatedResultOfEventSessionTemplateSyncHistoryItemDto> GetHistoryAsync(Guid sessionId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}
