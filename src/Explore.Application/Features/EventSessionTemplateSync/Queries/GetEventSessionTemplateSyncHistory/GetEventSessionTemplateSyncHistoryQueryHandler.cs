using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain;

namespace Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;

public sealed class GetEventSessionTemplateSyncHistoryQueryHandler
    : IQueryHandler<GetEventSessionTemplateSyncHistoryQuery, PaginatedResult<EventSessionTemplateSyncHistoryItemDto>>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IPrivateEventManagementReader? _privateManagement;

    public GetEventSessionTemplateSyncHistoryQueryHandler(IAuditLogRepository auditLogRepository, IPrivateEventManagementReader? privateManagement = null)
    {
        _auditLogRepository = auditLogRepository;
        _privateManagement = privateManagement;
    }

    public async Task<PaginatedResult<EventSessionTemplateSyncHistoryItemDto>> QueryAsync(
        GetEventSessionTemplateSyncHistoryQuery request,
        CancellationToken cancellationToken)
    {
        (int pageNumber, int pageSize) = PaginatedResult<EventSessionTemplateSyncHistoryItemDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var graph = _privateManagement is null ? null :
            await _privateManagement.ReadSessionHistoryAsync(request.EventSessionId, pageNumber, pageSize, cancellationToken);
        (IReadOnlyList<AuditLog> items, int totalCount) = graph is not null
            ? (graph.History.OrderByDescending(row => row.Timestamp).Skip(checked((pageNumber - 1) * pageSize)).Take(pageSize).ToArray(), graph.History.Count)
            : await _auditLogRepository.GetTemplateSyncHistoryAsync(
            nameof(EventSession),
            request.EventSessionId.ToString(),
            pageNumber,
            pageSize,
            cancellationToken);

        List<EventSessionTemplateSyncHistoryItemDto> mapped = items
            .Select(x => Map(x, request.EventSessionId))
            .ToList();

        return PaginatedResult<EventSessionTemplateSyncHistoryItemDto>.Create(mapped, totalCount, pageNumber, pageSize);
    }

    internal static EventSessionTemplateSyncHistoryItemDto Map(AuditLog auditLog, Guid eventSessionId)
    {
        AuditOldValues oldValues = Deserialize<AuditOldValues>(auditLog.OldValues) ?? new AuditOldValues();
        AuditNewValues newValues = Deserialize<AuditNewValues>(auditLog.NewValues) ?? new AuditNewValues();

        return new EventSessionTemplateSyncHistoryItemDto(
            eventSessionId,
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
