using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventLocationExactReadAudit : ITenantEntity
{
    private Guid _tenantId;

    private EventLocationExactReadAudit()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => SetTenantId(value);
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => SetTenantId(value);
    }

    public Guid EventLocationId { get; private set; }
    public Guid? RequesterUserId { get; private set; }
    public EventLocationExactReadPurposeEnum Purpose { get; private set; }
    public bool WasAuthorized { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public Guid? TraceId { get; private set; }

    public static EventLocationExactReadAudit Create(
        Guid tenantId,
        Guid eventLocationId,
        Guid requesterUserId,
        EventLocationExactReadPurposeEnum purpose,
        bool wasAuthorized,
        DateTime occurredAtUtc,
        Guid? correlationId,
        Guid? traceId)
    {
        RequireId(tenantId, nameof(tenantId));
        RequireId(eventLocationId, nameof(eventLocationId));
        RequireId(requesterUserId, nameof(requesterUserId));
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        RequireOptionalId(correlationId, nameof(correlationId));
        RequireOptionalId(traceId, nameof(traceId));
        if (correlationId is null && traceId is null)
        {
            throw new ArgumentException("An exact-read audit requires a correlation or trace id.", nameof(correlationId));
        }

        return new EventLocationExactReadAudit
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventLocationId = eventLocationId,
            RequesterUserId = requesterUserId,
            Purpose = purpose,
            WasAuthorized = wasAuthorized,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId,
            TraceId = traceId
        };
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }

    private static void RequireOptionalId(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must be non-empty when provided.", parameterName);
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }
    }

    private void SetTenantId(Guid value)
    {
        RequireId(value, nameof(TenantId));
        if (_tenantId != Guid.Empty && _tenantId != value)
        {
            throw new InvalidOperationException("Exact-read audit tenant identity is immutable.");
        }

        _tenantId = value;
    }
}
