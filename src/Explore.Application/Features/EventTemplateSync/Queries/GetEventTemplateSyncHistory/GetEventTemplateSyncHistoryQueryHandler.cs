using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateSyncHistory;

public sealed class GetEventTemplateSyncHistoryQueryHandler
    : IRequestHandler<GetEventTemplateSyncHistoryQuery, PaginatedResult<EventTemplateSyncHistoryItemDto>>
{
    private readonly IAuditLogRepository _auditLogRepository;

    public GetEventTemplateSyncHistoryQueryHandler(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task<PaginatedResult<EventTemplateSyncHistoryItemDto>> Handle(
        GetEventTemplateSyncHistoryQuery request,
        CancellationToken cancellationToken)
    {
        (int pageNumber, int pageSize) = PaginatedResult<EventTemplateSyncHistoryItemDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        (IReadOnlyList<AuditLog> items, int totalCount) = await _auditLogRepository.GetTemplateSyncHistoryAsync(
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
