using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;

public sealed class GetEventSessionTemplateSyncHistoryQueryHandler
    : IRequestHandler<GetEventSessionTemplateSyncHistoryQuery, PaginatedResult<EventSessionTemplateSyncHistoryItemDto>>
{
    private readonly IAuditLogRepository _auditLogRepository;

    public GetEventSessionTemplateSyncHistoryQueryHandler(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task<PaginatedResult<EventSessionTemplateSyncHistoryItemDto>> Handle(
        GetEventSessionTemplateSyncHistoryQuery request,
        CancellationToken cancellationToken)
    {
        (int pageNumber, int pageSize) = PaginatedResult<EventSessionTemplateSyncHistoryItemDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        (IReadOnlyList<AuditLog> items, int totalCount) = await _auditLogRepository.GetTemplateSyncHistoryAsync(
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

    private static EventSessionTemplateSyncHistoryItemDto Map(AuditLog auditLog, Guid eventSessionId)
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
