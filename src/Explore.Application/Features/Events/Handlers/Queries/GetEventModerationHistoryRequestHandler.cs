using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Queries;

public sealed class GetEventModerationHistoryRequestHandler(
    IEventRepository eventRepository,
    IEventModerationRecordRepository moderationRecordRepository)
    : IRequestHandler<GetEventModerationHistoryRequest, IReadOnlyList<EventModerationHistoryDto>?>
{
    public async Task<IReadOnlyList<EventModerationHistoryDto>?> Handle(
        GetEventModerationHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var @event = await eventRepository.GetById(request.Id);
        if (@event is null)
        {
            return null;
        }

        var records = await moderationRecordRepository.GetByEventAsync(
            @event.TenantId,
            @event.Id,
            cancellationToken);

        return records.Select(Map).ToArray();
    }

    private static EventModerationHistoryDto Map(EventModerationRecord record)
    {
        return new EventModerationHistoryDto
        {
            Id = record.Id,
            EventId = record.EventId,
            ModeratorUserId = record.ModeratorUserId,
            ActionKindId = (int)record.ActionKind,
            ActionKindName = record.ActionKind.ToString(),
            ReasonCode = record.ReasonCode,
            PreviousStatusId = record.PreviousStatusId,
            PreviousStatusName = ToEventStatusName(record.PreviousStatusId),
            ResultingStatusId = record.ResultingStatusId,
            ResultingStatusName = ToEventStatusName(record.ResultingStatusId),
            IsIrreversible = record.IsIrreversible,
            AllowsUnmoderation = record.AllowsUnmoderation,
            SourceModerationRecordId = record.SourceModerationRecordId,
            CorrelationId = record.CorrelationId,
            CreatedAt = record.CreatedAt
        };
    }

    private static string ToEventStatusName(int statusId)
    {
        return Enum.IsDefined(typeof(EventStatusEnum), statusId)
            ? ((EventStatusEnum)statusId).ToString()
            : "Unknown";
    }
}
