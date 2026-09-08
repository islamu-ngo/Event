using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventContactShareConsent : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private EventContactShareConsent()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public int SubjectTypeId { get; private set; }
    public ContactShareConsentSubjectType? SubjectType { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid? UserSubjectId { get; private set; }
    public User? UserSubject { get; private set; }
    public Guid? RegistrationPurchaserOrderId { get; private set; }
    public RegistrationOrder? RegistrationPurchaserOrder { get; private set; }
    public Guid? RegistrationParticipantId { get; private set; }
    public RegistrationParticipant? RegistrationParticipant { get; private set; }
    public Guid? GuestContactOrderId { get; private set; }
    public RegistrationOrder? GuestContactOrder { get; private set; }
    public Guid RecipientActorId { get; private set; }
    public Actor? RecipientActor { get; private set; }
    public string PurposeCode { get; private set; } = string.Empty;
    public ConsentStatus Status { get; private set; }
    public string EmailSnapshot { get; private set; } = string.Empty;
    public string EmailNormalizedSnapshot { get; private set; } = string.Empty;
    public string ConsentTextSnapshot { get; private set; } = string.Empty;
    public string ConsentUiVersion { get; private set; } = string.Empty;
    public DateTime GrantedAt { get; private set; }
    public DateTime? WithdrawnAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static EventContactShareConsent Grant(
        Guid tenantId,
        ContactShareConsentSubjectTypeEnum subjectType,
        Guid subjectId,
        Guid recipientActorId,
        string purposeCode,
        string emailSnapshot,
        string consentTextSnapshot,
        string consentUiVersion,
        DateTime grantedAt)
    {
        ValidateIdentity(tenantId, subjectType, subjectId, recipientActorId, purposeCode, emailSnapshot, consentTextSnapshot,
            consentUiVersion, grantedAt);

        EventContactShareConsent consent = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            SubjectTypeId = (int)subjectType,
            SubjectId = subjectId,
            RecipientActorId = recipientActorId,
            PurposeCode = NormalizeCode(purposeCode),
            Status = ConsentStatus.Granted,
            EmailSnapshot = emailSnapshot.Trim(),
            EmailNormalizedSnapshot = NormalizeEmail(emailSnapshot),
            ConsentTextSnapshot = consentTextSnapshot.Trim(),
            ConsentUiVersion = consentUiVersion.Trim(),
            GrantedAt = grantedAt,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = grantedAt
        };
        consent.SetTypedSubject(subjectType, subjectId);
        return consent;
    }

    public EventContactShareConsentHistory CreateGrantHistory(
        Guid? sourceEventId,
        Guid? sourceRegistrationOrderId,
        Guid? actorId,
        Guid? userId,
        DateTime occurredAt) => EventContactShareConsentHistory.Create(this,
        EventContactShareConsentHistoryOperationEnum.Grant, sourceEventId, sourceRegistrationOrderId, actorId, userId, occurredAt);

    public EventContactShareConsentHistory Withdraw(Guid? actorId, Guid? userId, DateTime withdrawnAt)
    {
        if (withdrawnAt == default || withdrawnAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Withdraw time must be UTC.", nameof(withdrawnAt));
        }

        if (Status == ConsentStatus.Withdrawn)
        {
            return EventContactShareConsentHistory.Create(this, EventContactShareConsentHistoryOperationEnum.Withdraw,
                null, null, actorId, userId, withdrawnAt);
        }

        Status = ConsentStatus.Withdrawn;
        WithdrawnAt = withdrawnAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return EventContactShareConsentHistory.Create(this, EventContactShareConsentHistoryOperationEnum.Withdraw,
            null, null, actorId, userId, withdrawnAt);
    }

    public EventContactShareConsentHistory Regrant(
        string emailSnapshot,
        string consentTextSnapshot,
        string consentUiVersion,
        Guid? sourceEventId,
        Guid? sourceRegistrationOrderId,
        Guid? actorId,
        Guid? userId,
        DateTime grantedAt)
    {
        ValidateSnapshot(emailSnapshot, consentTextSnapshot, consentUiVersion, grantedAt);
        EmailSnapshot = emailSnapshot.Trim();
        EmailNormalizedSnapshot = NormalizeEmail(emailSnapshot);
        ConsentTextSnapshot = consentTextSnapshot.Trim();
        ConsentUiVersion = consentUiVersion.Trim();
        Status = ConsentStatus.Granted;
        GrantedAt = grantedAt;
        WithdrawnAt = null;
        ConcurrencyStamp = Guid.CreateVersion7();
        return EventContactShareConsentHistory.Create(this, EventContactShareConsentHistoryOperationEnum.Regrant,
            sourceEventId, sourceRegistrationOrderId, actorId, userId, grantedAt);
    }

    private void SetTypedSubject(ContactShareConsentSubjectTypeEnum subjectType, Guid subjectId)
    {
        UserSubjectId = subjectType == ContactShareConsentSubjectTypeEnum.User ? subjectId : null;
        RegistrationPurchaserOrderId = subjectType == ContactShareConsentSubjectTypeEnum.RegistrationPurchaser ? subjectId : null;
        RegistrationParticipantId = subjectType == ContactShareConsentSubjectTypeEnum.RegistrationParticipant ? subjectId : null;
        GuestContactOrderId = subjectType == ContactShareConsentSubjectTypeEnum.GuestContact ? subjectId : null;
    }

    private static void ValidateIdentity(Guid tenantId, ContactShareConsentSubjectTypeEnum subjectType, Guid subjectId,
        Guid recipientActorId, string purposeCode, string emailSnapshot, string consentTextSnapshot, string consentUiVersion,
        DateTime occurredAt)
    {
        if (tenantId == Guid.Empty || subjectId == Guid.Empty || recipientActorId == Guid.Empty || !Enum.IsDefined(subjectType) ||
            string.IsNullOrWhiteSpace(purposeCode))
        {
            throw new ArgumentException("Consent scope requires tenant, typed subject, recipient actor, and purpose.");
        }

        ValidateSnapshot(emailSnapshot, consentTextSnapshot, consentUiVersion, occurredAt);
    }

    private static void ValidateSnapshot(string emailSnapshot, string consentTextSnapshot, string consentUiVersion, DateTime occurredAt)
    {
        if (occurredAt == default || occurredAt.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(emailSnapshot) ||
            string.IsNullOrWhiteSpace(consentTextSnapshot) || string.IsNullOrWhiteSpace(consentUiVersion))
        {
            throw new ArgumentException("Consent snapshots and UTC time are required.");
        }
    }

    private static string NormalizeEmail(string value) => value.Trim().ToLowerInvariant();

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
}
