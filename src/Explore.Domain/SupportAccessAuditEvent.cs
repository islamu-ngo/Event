using System.Text.Json;
using Explore.Domain.Enums;

namespace Explore.Domain;

public class SupportAccessAuditEvent
{
    public const int MaxRouteNameLength = 200;
    public const int MaxRequestNameLength = 200;
    public const int MaxResourceKindLength = 200;
    public const int MaxResourceIdLength = 200;
    public const int MaxActionLength = 100;
    public const int MaxOutcomeLength = 100;
    public const int MaxCorrelationIdLength = 100;
    public const int MaxTraceIdLength = 100;
    public const int MaxMetadataJsonLength = 8000;

    public Guid Id { get; private set; }
    public Guid SupportAccessSessionId { get; private set; }
    public SupportAccessSession SupportAccessSession { get; private set; } = null!;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public int EventTypeId { get; private set; }
    public SupportAccessAuditEventType EventType { get; private set; } = null!;
    public Guid? ActorUserId { get; private set; }
    public User? ActorUser { get; private set; }
    public Guid TargetTenantId { get; private set; }
    public Tenant TargetTenant { get; private set; } = null!;
    public Guid? TargetTenantUserId { get; private set; }
    public TenantUser? TargetTenantUser { get; private set; }
    public string? RouteName { get; private set; }
    public string? RequestName { get; private set; }
    public string? ResourceKind { get; private set; }
    public string? ResourceId { get; private set; }
    public string? Action { get; private set; }
    public string Outcome { get; private set; } = string.Empty;
    public int? HttpStatusCode { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? TraceId { get; private set; }
    public string? SanitizedMetadataJson { get; private set; }

    public static SupportAccessAuditEvent Create(
        Guid supportAccessSessionId,
        SupportAccessAuditEventTypeEnum eventType,
        Guid actorUserId,
        Guid targetTenantId,
        string outcome,
        DateTimeOffset occurredAtUtc,
        Guid? targetTenantUserId = null,
        string? routeName = null,
        string? requestName = null,
        string? resourceKind = null,
        string? resourceId = null,
        string? action = null,
        int? httpStatusCode = null,
        string? correlationId = null,
        string? traceId = null,
        string? sanitizedMetadataJson = null)
    {
        EnsureNotEmpty(supportAccessSessionId, nameof(supportAccessSessionId));
        EnsureNotEmpty(actorUserId, nameof(actorUserId));
        EnsureNotEmpty(targetTenantId, nameof(targetTenantId));
        EnsureOptionalNotEmpty(targetTenantUserId, nameof(targetTenantUserId));

        if (httpStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(httpStatusCode), "HTTP status code must be between 100 and 599.");
        }

        return new SupportAccessAuditEvent
        {
            Id = Guid.CreateVersion7(),
            SupportAccessSessionId = supportAccessSessionId,
            EventTypeId = (int)eventType,
            ActorUserId = actorUserId,
            TargetTenantId = targetTenantId,
            TargetTenantUserId = targetTenantUserId,
            OccurredAtUtc = occurredAtUtc,
            RouteName = NormalizeOptional(routeName, MaxRouteNameLength, nameof(routeName)),
            RequestName = NormalizeOptional(requestName, MaxRequestNameLength, nameof(requestName)),
            ResourceKind = NormalizeOptional(resourceKind, MaxResourceKindLength, nameof(resourceKind)),
            ResourceId = NormalizeOptional(resourceId, MaxResourceIdLength, nameof(resourceId)),
            Action = NormalizeOptional(action, MaxActionLength, nameof(action)),
            Outcome = NormalizeRequired(outcome, MaxOutcomeLength, nameof(outcome)),
            HttpStatusCode = httpStatusCode,
            CorrelationId = NormalizeOptional(correlationId, MaxCorrelationIdLength, nameof(correlationId)),
            TraceId = NormalizeOptional(traceId, MaxTraceIdLength, nameof(traceId)),
            SanitizedMetadataJson = NormalizeOptionalJson(sanitizedMetadataJson, MaxMetadataJsonLength, nameof(sanitizedMetadataJson))
        };
    }

    public static SupportAccessAuditEvent CreateLifecycleEvent(
        SupportAccessSession session,
        SupportAccessAuditEventTypeEnum eventType,
        string outcome,
        DateTimeOffset occurredAtUtc,
        string? correlationId = null,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        return Create(
            session.Id,
            eventType,
            session.ActorUserId ?? throw new InvalidOperationException("Support-access actor identity is unavailable."),
            session.TargetTenantId,
            outcome,
            occurredAtUtc,
            session.TargetTenantUserId,
            correlationId: correlationId,
            traceId: traceId);
    }

    private static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }

    private static void EnsureOptionalNotEmpty(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required when provided.", parameterName);
        }
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

    private static string? NormalizeOptionalJson(string? value, int maxLength, string parameterName)
    {
        string? normalized = NormalizeOptional(value, maxLength, parameterName);
        if (normalized is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(normalized);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Value must be valid JSON.", parameterName, ex);
        }

        return normalized;
    }
}
