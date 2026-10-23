using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;

public sealed class GetManagedSessionTemplateSyncHistoryQueryHandler(IPrivateEventManagementReader reader)
    : IQueryHandler<GetManagedSessionTemplateSyncHistoryQuery, PaginatedResult<EventSessionTemplateSyncHistoryItemDto>>
{
    public async Task<PaginatedResult<EventSessionTemplateSyncHistoryItemDto>> QueryAsync(
        GetManagedSessionTemplateSyncHistoryQuery request, CancellationToken cancellationToken)
    {
        var (page, size) = PaginatedResult<EventSessionTemplateSyncHistoryItemDto>
            .NormalizeParameters(request.PageNumber, request.PageSize);
        var graph = await reader.ReadAsync(new(PrivateEventManagementReadKind.SessionSyncContext,
            EventId: request.EventId, SessionId: request.SessionId, PageNumber: page, PageSize: size),
            cancellationToken);
        if (!graph.Sessions.Any(row => row.Id == request.SessionId && row.EventId == request.EventId))
            throw new NotFoundException(nameof(EventSession), request.SessionId);
        var items = graph.History.OrderByDescending(row => row.Timestamp)
            .Skip(checked((page - 1) * size)).Take(size)
            .Select(row => GetEventSessionTemplateSyncHistoryQueryHandler.Map(row, request.SessionId)).ToList();
        return PaginatedResult<EventSessionTemplateSyncHistoryItemDto>.Create(items, graph.History.Count, page, size);
    }
}
