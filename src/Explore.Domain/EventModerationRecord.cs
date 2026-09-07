using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventModerationRecord : ITenantEntity
{
    private const int MaxReasonCodeLength = 100;
    private const int MaxCorrelationIdLength = 100;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid EventId { get; private set; }
    public Event Event { get; private set; } = null!;
    public Guid? ModeratorUserId { get; private set; }
    public EventModerationActionKind ActionKind { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public int PreviousStatusId { get; private set; }
    public int ResultingStatusId { get; private set; }
    public bool IsIrreversible { get; private set; }
    public Guid? SourceModerationRecordId { get; private set; }
    public EventModerationRecord? SourceModerationRecord { get; private set; }
    public Guid? SourceReportId { get; private set; }
    public EventReport? SourceReport { get; private set; }
    public Guid? SourceReportDecisionId { get; private set; }
    public EventReportDecision? SourceReportDecision { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool AllowsUnmoderation =>
        ActionKind == EventModerationActionKind.LightModerated
        && !IsIrreversible
        && ResultingStatusId == (int)EventStatusEnum.Moderated;

    public static EventModerationRecord CreateLightModeration(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid? moderatorUserId,
        string reasonCode,
        int previousStatusId,
        string? correlationId,
        DateTimeOffset createdAt)
    {
        return Create(
            id,
            tenantId,
            eventId,
            moderatorUserId,
            EventModerationActionKind.LightModerated,
            reasonCode,
            previousStatusId,
            (int)EventStatusEnum.Moderated,
            isIrreversible: false,
            sourceModerationRecordId: null,
            correlationId,
            createdAt);
    }

    public static EventModerationRecord CreateHeavyRedaction(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid? moderatorUserId,
        string reasonCode,
        int previousStatusId,
        string? correlationId,
        DateTimeOffset createdAt)
    {
        return Create(
            id,
            tenantId,
            eventId,
            moderatorUserId,
            EventModerationActionKind.HeavyRedacted,
            reasonCode,
            previousStatusId,
            (int)EventStatusEnum.Moderated,
            isIrreversible: true,
            sourceModerationRecordId: null,
            correlationId,
            createdAt);
    }

    public static EventModerationRecord CreateUnmoderation(
        Guid id,
        EventModerationRecord sourceRecord,
        Guid? moderatorUserId,
        string reasonCode,
        string? correlationId,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(sourceRecord);

        if (!sourceRecord.AllowsUnmoderation)
        {
            throw new InvalidOperationException("Only reversible light moderation records can be unmoderated.");
        }

        return Create(
            id,
            sourceRecord.TenantId,
            sourceRecord.EventId,
            moderatorUserId,
            EventModerationActionKind.Unmoderated,
            reasonCode,
            (int)EventStatusEnum.Moderated,
            (int)EventStatusEnum.Published,
            isIrreversible: false,
            sourceRecord.Id,
            correlationId,
            createdAt);
    }

    private static EventModerationRecord Create(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid? moderatorUserId,
        EventModerationActionKind actionKind,
        string reasonCode,
        int previousStatusId,
        int resultingStatusId,
        bool isIrreversible,
        Guid? sourceModerationRecordId,
        string? correlationId,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Moderation record id is required.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event id is required.", nameof(eventId));
        }

        if (moderatorUserId == Guid.Empty)
        {
            throw new ArgumentException("Moderator user id is required.", nameof(moderatorUserId));
        }

        string normalizedReasonCode = NormalizeRequired(reasonCode, MaxReasonCodeLength, nameof(reasonCode));
        string? normalizedCorrelationId = NormalizeOptional(correlationId, MaxCorrelationIdLength, nameof(correlationId));

        return new EventModerationRecord
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            ModeratorUserId = moderatorUserId,
            ActionKind = actionKind,
            ReasonCode = normalizedReasonCode,
            PreviousStatusId = previousStatusId,
            ResultingStatusId = resultingStatusId,
            IsIrreversible = isIrreversible,
            SourceModerationRecordId = sourceModerationRecordId,
            CorrelationId = normalizedCorrelationId,
            CreatedAt = createdAt
        };
    }

    public void LinkSourceReportDecision(Guid sourceReportId, Guid sourceReportDecisionId)
    {
        if (sourceReportId == Guid.Empty)
        {
            throw new ArgumentException("Source report id is required.", nameof(sourceReportId));
        }

        if (sourceReportDecisionId == Guid.Empty)
        {
            throw new ArgumentException("Source report decision id is required.", nameof(sourceReportDecisionId));
        }

        SourceReportId = sourceReportId;
        SourceReportDecisionId = sourceReportDecisionId;
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        string normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }
}
