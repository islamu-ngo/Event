using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationParticipant : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private Guid _tenantId;

    private RegistrationParticipant()
    {
    }

    private RegistrationParticipant(
        Guid id,
        Guid tenantId,
        Guid registrationOrderId,
        Guid? linkedUserId,
        ParticipantTypeEnum participantType,
        RegistrationParticipant? guardian)
    {
        Id = id;
        TenantId = tenantId;
        RegistrationOrderId = registrationOrderId;
        LinkedUserId = linkedUserId;
        ParticipantTypeId = (int)participantType;
        GuardianParticipantId = guardian?.Id;
        GuardianParticipant = guardian;
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(RegistrationParticipant));
    }

    public Guid RegistrationOrderId { get; private set; }

    public RegistrationOrder? RegistrationOrder { get; private set; }

    public Guid? LinkedUserId { get; private set; }

    public User? LinkedUser { get; private set; }

    public int ParticipantTypeId { get; private set; }

    public ParticipantType? ParticipantType { get; private set; }

    public Guid? GuardianParticipantId { get; private set; }

    public RegistrationParticipant? GuardianParticipant { get; private set; }

    public RegistrationParticipantPii? Pii { get; private set; }

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public static RegistrationParticipant Create(
        Guid tenantId,
        Guid registrationOrderId,
        Guid? linkedUserId,
        ParticipantTypeEnum participantType,
        RegistrationParticipant? guardian) => Create(
        Guid.CreateVersion7(), tenantId, registrationOrderId, linkedUserId, participantType, guardian);

    public static RegistrationParticipant Create(
        Guid id,
        Guid tenantId,
        Guid registrationOrderId,
        Guid? linkedUserId,
        ParticipantTypeEnum participantType,
        RegistrationParticipant? guardian)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || registrationOrderId == Guid.Empty ||
            linkedUserId == Guid.Empty || !Enum.IsDefined(participantType))
        {
            throw new ArgumentException("Participant identity and type are invalid.");
        }

        bool requiresGuardian = participantType is ParticipantTypeEnum.Child or ParticipantTypeEnum.Dependent;
        if (requiresGuardian != (guardian is not null))
        {
            throw new ArgumentException("Only child and dependent participants require a guardian.", nameof(guardian));
        }

        if (guardian is not null &&
            (guardian.Id == id || guardian.TenantId != tenantId || guardian.RegistrationOrderId != registrationOrderId ||
             guardian.ParticipantTypeId != (int)ParticipantTypeEnum.Adult))
        {
            throw new ArgumentException("A guardian must be a different adult participant in the same order.", nameof(guardian));
        }

        return new RegistrationParticipant(id, tenantId, registrationOrderId, linkedUserId, participantType, guardian);
    }

    internal void AttachToOrder(RegistrationOrder order)
    {
        if (RegistrationOrder is not null && !ReferenceEquals(RegistrationOrder, order))
        {
            throw new InvalidOperationException("Participant is already attached to another order graph.");
        }

        RegistrationOrder = order;
    }

    public void SetPii(RegistrationParticipantPii pii)
    {
        ArgumentNullException.ThrowIfNull(pii);
        if (pii.RegistrationParticipantId != Id || pii.TenantId != TenantId)
        {
            throw new ArgumentException("Participant PII does not belong to this participant.", nameof(pii));
        }

        Pii = pii;
    }

    public void ClaimBy(
        Guid linkedUserId,
        Guid concurrencyStamp)
    {
        if (linkedUserId == Guid.Empty
            || concurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException(
                "Participant subject and concurrency are required.");
        }
        if (LinkedUserId.HasValue
            && LinkedUserId != linkedUserId)
        {
            throw new InvalidOperationException(
                "Participant is already linked to another subject.");
        }

        LinkedUserId = linkedUserId;
        ConcurrencyStamp = concurrencyStamp;
    }

    public void Update(ParticipantTypeEnum participantType, RegistrationParticipant? guardian, Guid concurrencyStamp)
    {
        if (!Enum.IsDefined(participantType) || concurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException("Participant type and concurrency stamp are required.");
        }

        bool requiresGuardian = participantType is ParticipantTypeEnum.Child or ParticipantTypeEnum.Dependent;
        if (requiresGuardian != (guardian is not null) || guardian is not null &&
            (guardian.Id == Id || guardian.TenantId != TenantId || guardian.RegistrationOrderId != RegistrationOrderId ||
             guardian.ParticipantTypeId != (int)ParticipantTypeEnum.Adult))
        {
            throw new ArgumentException("Child and dependent participants require a different adult guardian from the same order.", nameof(guardian));
        }

        ParticipantTypeId = (int)participantType;
        GuardianParticipantId = guardian?.Id;
        GuardianParticipant = guardian;
        ConcurrencyStamp = concurrencyStamp;
    }
}
