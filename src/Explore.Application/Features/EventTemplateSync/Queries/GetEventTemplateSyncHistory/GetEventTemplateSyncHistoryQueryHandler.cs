using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain;

namespace Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateSyncHistory;

public sealed class GetEventTemplateSyncHistoryQueryHandler
    : IQueryHandler<GetEventTemplateSyncHistoryQuery, PaginatedResult<EventTemplateSyncHistoryItemDto>>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IPrivateEventManagementReader? _privateManagement;

    public GetEventTemplateSyncHistoryQueryHandler(IAuditLogRepository auditLogRepository, IPrivateEventManagementReader? privateManagement = null)
    {
        _auditLogRepository = auditLogRepository;
        _privateManagement = privateManagement;
    }

    public async Task<PaginatedResult<EventTemplateSyncHistoryItemDto>> QueryAsync(
        GetEventTemplateSyncHistoryQuery request,
        CancellationToken cancellationToken)
    {
        (int pageNumber, int pageSize) = PaginatedResult<EventTemplateSyncHistoryItemDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var graph = _privateManagement is null ? null :
            await _privateManagement.ReadAsync(new(PrivateEventManagementReadKind.EventSyncHistory, request.EventId), cancellationToken);
        (IReadOnlyList<AuditLog> items, int totalCount) = graph is not null
            ? (graph.History.OrderByDescending(row => row.Timestamp).Skip(checked((pageNumber - 1) * pageSize)).Take(pageSize).ToArray(), graph.History.Count)
            : await _auditLogRepository.GetTemplateSyncHistoryAsync(
            nameof(Event),
            request.EventId.ToString(),
            pageNumber,
            pageSize,
            cancellationToken);

        List<EventTemplateSyncHistoryItemDto> mapped = items
            .Select(x => Map(x, request.EventId))
            .ToList();

        return PaginatedResult<EventTemplateSyncHistoryItemDto>.Create(mapped, totalCount, pageNumber, pageSize);
    }

    private static EventTemplateSyncHistoryItemDto Map(AuditLog auditLog, Guid eventId)
    {
        AuditOldValues oldValues = Deserialize<AuditOldValues>(auditLog.OldValues) ?? new AuditOldValues();
        AuditNewValues newValues = Deserialize<AuditNewValues>(auditLog.NewValues) ?? new AuditNewValues();

        return new EventTemplateSyncHistoryItemDto(
            eventId,
            oldValues.BaseVersion,
            newValues.TargetVersion,
            newValues.Applied,
            newValues.Skipped,
            newValues.Conflicts,
            auditLog.ActorId,
            new DateTimeOffset(DateTime.SpecifyKind(auditLog.Timestamp, DateTimeKind.Utc)));
    }

    private static T? Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json);

    private sealed class AuditOldValues
    {
        public int BaseVersion { get; init; }
    }

    private sealed class AuditNewValues
    {
        public int TargetVersion { get; init; }
        public IReadOnlyList<string> Applied { get; init; } = [];
        public IReadOnlyList<string> Skipped { get; init; } = [];
        public IReadOnlyList<SyncConflictDto> Conflicts { get; init; } = [];
    }
}
