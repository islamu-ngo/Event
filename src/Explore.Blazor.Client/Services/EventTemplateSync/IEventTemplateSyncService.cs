using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.EventTemplateSync;

public interface IEventTemplateSyncService
{
    Task<HalResourceOfTemplateDiffDto> GetDiffAsync(Guid eventId, int templateVersion, CancellationToken cancellationToken = default);
    Task<TemplateSyncOutcomeDto> ApplySyncAsync(Guid eventId, EventTemplateSyncApplyRequest request, CancellationToken cancellationToken = default);
    Task<PaginatedResultOfEventTemplateSyncHistoryItemDto> GetHistoryAsync(Guid eventId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}
