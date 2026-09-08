using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventContactShareConsentHistory : ITenantEntity, IAuditableEntity
{
    private EventContactShareConsentHistory()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid ConsentId { get; private set; }
    public EventContactShareConsent? Consent { get; private set; }
    public int OperationId { get; private set; }
    public ConsentStatus StatusSnapshot { get; private set; }
    public int SubjectTypeId { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid RecipientActorId { get; private set; }
    public string PurposeCodeSnapshot { get; private set; } = string.Empty;
    public string EmailSnapshot { get; private set; } = string.Empty;
    public string EmailNormalizedSnapshot { get; private set; } = string.Empty;
    public string ConsentTextSnapshot { get; private set; } = string.Empty;
    public string ConsentUiVersionSnapshot { get; private set; } = string.Empty;
    public Guid? SourceEventId { get; private set; }
    public Event? SourceEvent { get; private set; }
    public Guid? SourceRegistrationOrderId { get; private set; }
    public RegistrationOrder? SourceRegistrationOrder { get; private set; }
    public Guid? ActorId { get; private set; }
    public Actor? Actor { get; private set; }
    public Guid? UserId { get; private set; }
    public User? User { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static EventContactShareConsentHistory Create(
        EventContactShareConsent consent,
        EventContactShareConsentHistoryOperationEnum operation,
        Guid? sourceEventId,
        Guid? sourceRegistrationOrderId,
        Guid? actorId,
        Guid? userId,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(consent);
        if (!Enum.IsDefined(operation) || occurredAt == default || occurredAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("History operation and UTC time are required.");
        }

        return new EventContactShareConsentHistory
        {
            Id = Guid.CreateVersion7(),
            TenantId = consent.TenantId,
            ConsentId = consent.Id,
            Consent = consent,
            OperationId = (int)operation,
            StatusSnapshot = consent.Status,
            SubjectTypeId = consent.SubjectTypeId,
            SubjectId = consent.SubjectId,
            RecipientActorId = consent.RecipientActorId,
            PurposeCodeSnapshot = consent.PurposeCode,
            EmailSnapshot = consent.EmailSnapshot,
            EmailNormalizedSnapshot = consent.EmailNormalizedSnapshot,
            ConsentTextSnapshot = consent.ConsentTextSnapshot,
            ConsentUiVersionSnapshot = consent.ConsentUiVersion,
            SourceEventId = sourceEventId,
            SourceRegistrationOrderId = sourceRegistrationOrderId,
            ActorId = actorId,
            UserId = userId,
            OccurredAt = occurredAt,
            CreatedAt = occurredAt
        };
    }
}
