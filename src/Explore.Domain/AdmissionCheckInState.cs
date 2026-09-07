using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class AdmissionCheckInState : ITenantEntity, IConcurrencyAware
{
    private Guid _tenantId;

    private AdmissionCheckInState()
    {
    }

    private AdmissionCheckInState(
        Guid id,
        Guid tenantId,
        Guid admissionTicketId,
        Guid admissionTargetId,
        Guid? activeCheckInEventId,
        int entryCount,
        long lastSequence,
        Guid concurrencyStamp)
    {
        Id = id;
        TenantId = tenantId;
        AdmissionTicketId = admissionTicketId;
        AdmissionTargetId = admissionTargetId;
        ActiveCheckInEventId = activeCheckInEventId;
        EntryCount = entryCount;
        LastSequence = lastSequence;
        ConcurrencyStamp = concurrencyStamp;
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionCheckInState));
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionCheckInState));
    }

    public Guid AdmissionTicketId { get; private set; }
    public Guid AdmissionTargetId { get; private set; }
    public Guid? ActiveCheckInEventId { get; private set; }
    public int EntryCount { get; private set; }
    public long LastSequence { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public static AdmissionCheckInState Create(
        Guid id,
        AdmissionTicket ticket,
        AdmissionTarget target)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(target);
        if (ticket.TenantId != target.TenantId || ticket.EventId != target.EventId)
        {
            throw new ArgumentException("Admission state ticket and target must share one tenant and event authority.");
        }

        return Rehydrate(
            id,
            ticket.TenantId,
            ticket.Id,
            target.Id,
            null,
            0,
            0,
            Guid.CreateVersion7());
    }

    public static AdmissionCheckInState Rehydrate(
        Guid id,
        Guid tenantId,
        Guid admissionTicketId,
        Guid admissionTargetId,
        Guid? activeCheckInEventId,
        int entryCount,
        long lastSequence,
        Guid concurrencyStamp)
    {
        RequireUuidV7(id, nameof(id));
        RequireUuidV7(tenantId, nameof(tenantId));
        RequireUuidV7(admissionTicketId, nameof(admissionTicketId));
        RequireUuidV7(admissionTargetId, nameof(admissionTargetId));
        RequireUuidV7(concurrencyStamp, nameof(concurrencyStamp));
        if (activeCheckInEventId.HasValue)
        {
            RequireUuidV7(activeCheckInEventId.Value, nameof(activeCheckInEventId));
        }

        if (entryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryCount));
        }

        if (lastSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastSequence));
        }

        long expectedSequence = entryCount == 0
            ? 0
            : activeCheckInEventId.HasValue
                ? checked((entryCount * 2L) - 1L)
                : checked(entryCount * 2L);
        if (lastSequence != expectedSequence || (entryCount == 0 && activeCheckInEventId.HasValue))
        {
            throw new ArgumentException("Admission state active fact, entry count, and sequence are inconsistent.");
        }

        return new AdmissionCheckInState(
            id,
            tenantId,
            admissionTicketId,
            admissionTargetId,
            activeCheckInEventId,
            entryCount,
            lastSequence,
            concurrencyStamp);
    }

    internal AdmissionCheckInState Project(Guid? activeCheckInEventId, int entryCount, long lastSequence) =>
        Rehydrate(
            Id,
            TenantId,
            AdmissionTicketId,
            AdmissionTargetId,
            activeCheckInEventId,
            entryCount,
            lastSequence,
            ConcurrencyStamp);

    private static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Admission state identity must be an RFC 4122 UUIDv7 value.", parameterName);
        }
    }
}
