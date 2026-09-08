using Explore.Application.DTOs.SupportAccess;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.SupportAccess;

public static class SupportAccessMapper
{
    public static SupportAccessSessionDto ToDto(SupportAccessSession session, DateTimeOffset nowUtc)
    {
        var mode = (SupportAccessModeEnum)session.ModeId;
        var status = (SupportAccessSessionStatusEnum)session.StatusId;

        return new SupportAccessSessionDto
        {
            Id = session.Id,
            ActorUserId = session.ActorUserId,
            TargetTenantId = session.TargetTenantId,
            TargetTenantUserId = session.TargetTenantUserId,
            StatusId = session.StatusId,
            StatusName = status.ToString(),
            ModeId = session.ModeId,
            ModeName = mode.ToString(),
            AllowsWrites = session.AllowsWrites && session.IsActiveAt(nowUtc),
            ReasonCode = session.ReasonCode,
            TicketReference = session.TicketReference,
            ApprovedByUserId = session.ApprovedByUserId,
            StartedAtUtc = session.StartedAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc,
            EndedAtUtc = session.EndedAtUtc,
            EndReasonId = session.EndReasonId,
            EndReasonName = session.EndReasonId.HasValue
                ? ((SupportAccessEndReasonEnum)session.EndReasonId.Value).ToString()
                : null,
            IsActive = session.IsActiveAt(nowUtc)
        };
    }

    public static SupportAccessAuditEventDto ToDto(SupportAccessAuditEvent auditEvent)
    {
        return new SupportAccessAuditEventDto
        {
            Id = auditEvent.Id,
            SupportAccessSessionId = auditEvent.SupportAccessSessionId,
            OccurredAtUtc = auditEvent.OccurredAtUtc,
            EventTypeId = auditEvent.EventTypeId,
            EventTypeName = ((SupportAccessAuditEventTypeEnum)auditEvent.EventTypeId).ToString(),
            ActorUserId = auditEvent.ActorUserId,
            TargetTenantId = auditEvent.TargetTenantId,
            TargetTenantUserId = auditEvent.TargetTenantUserId,
            RouteName = auditEvent.RouteName,
            RequestName = auditEvent.RequestName,
            ResourceKind = auditEvent.ResourceKind,
            ResourceId = auditEvent.ResourceId,
            Action = auditEvent.Action,
            Outcome = auditEvent.Outcome,
            HttpStatusCode = auditEvent.HttpStatusCode,
            CorrelationId = auditEvent.CorrelationId,
            TraceId = auditEvent.TraceId,
            SanitizedMetadataJson = auditEvent.SanitizedMetadataJson
        };
    }
}
