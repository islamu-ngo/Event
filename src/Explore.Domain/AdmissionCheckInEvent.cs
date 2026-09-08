using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class AdmissionCheckInEvent : ITenantEntity
{
    private Guid _tenantId;

    private AdmissionCheckInEvent()
    {
    }

    internal AdmissionCheckInEvent(
        Guid id,
        Guid tenantId,
        Guid admissionTicketId,
        Guid admissionTargetId,
        long sequence,
        AdmissionCheckInActionEnum action,
        Guid? actorId,
        Guid? scannerCapabilityId,
        AdmissionCheckInUndoReasonCodeEnum? reasonCode,
        DateTime occurredAtUtc,
        Guid? compensatedCheckInEventId)
    {
        Id = id;
        TenantId = tenantId;
        AdmissionTicketId = admissionTicketId;
        AdmissionTargetId = admissionTargetId;
        Sequence = sequence;
        AdmissionCheckInActionId = (int)action;
        ActorId = actorId;
        ScannerCapabilityId = scannerCapabilityId;
        AdmissionCheckInUndoReasonCodeId = reasonCode.HasValue ? (int)reasonCode.Value : null;
        OccurredAtUtc = occurredAtUtc;
        CompensatedCheckInEventId = compensatedCheckInEventId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionCheckInEvent));
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionCheckInEvent));
    }

    public Guid AdmissionTicketId { get; private set; }
    public Guid AdmissionTargetId { get; private set; }
    public long Sequence { get; private set; }
    public int AdmissionCheckInActionId { get; private set; }
    public Guid? ActorId { get; private set; }
    public Guid? ScannerCapabilityId { get; private set; }
    public int? AdmissionCheckInUndoReasonCodeId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? CompensatedCheckInEventId { get; private set; }
}
