using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class AdmissionTarget : ITenantEntity, IConcurrencyAware
{
    private Guid _tenantId;

    private AdmissionTarget()
    {
    }

    private AdmissionTarget(
        Guid id,
        Guid tenantId,
        Guid eventId,
        AdmissionTargetTypeEnum targetType,
        Guid? eventDayId,
        Guid? eventSessionId)
    {
        Id = id;
        TenantId = tenantId;
        EventId = eventId;
        AdmissionTargetTypeId = (int)targetType;
        AdmissionOperationalStatusId = (int)AdmissionOperationalStatusEnum.Active;
        EventDayId = eventDayId;
        EventSessionId = eventSessionId;
        ScopeId = targetType switch
        {
            AdmissionTargetTypeEnum.Event => eventId,
            AdmissionTargetTypeEnum.EventDay => eventDayId!.Value,
            AdmissionTargetTypeEnum.EventSession => eventSessionId!.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(targetType))
        };
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionTarget));
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionTarget));
    }

    public Guid EventId { get; private set; }
    public int AdmissionTargetTypeId { get; private set; }
    public int AdmissionOperationalStatusId { get; private set; }
    public Guid ScopeId { get; private set; }
    public Guid? EventDayId { get; private set; }
    public Guid? EventSessionId { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public bool IsOperational =>
        AdmissionOperationalStatusId == (int)AdmissionOperationalStatusEnum.Active;

    public void Stop()
    {
        AdmissionOperationalStatusId = (int)AdmissionOperationalStatusEnum.Stopped;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Restore()
    {
        AdmissionOperationalStatusId = (int)AdmissionOperationalStatusEnum.Active;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public static AdmissionTarget Create(
        Guid id,
        Guid tenantId,
        Guid eventId,
        AdmissionTargetTypeEnum targetType,
        Guid? eventDayId,
        Guid? eventSessionId)
    {
        RequireUuidV7(id, nameof(id));
        RequireUuidV7(tenantId, nameof(tenantId));
        RequireUuidV7(eventId, nameof(eventId));
        if (!Enum.IsDefined(targetType))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType));
        }

        switch (targetType)
        {
            case AdmissionTargetTypeEnum.Event when eventDayId is null && eventSessionId is null:
                break;
            case AdmissionTargetTypeEnum.EventDay when eventDayId.HasValue && eventSessionId is null:
                RequireUuidV7(eventDayId.Value, nameof(eventDayId));
                break;
            case AdmissionTargetTypeEnum.EventSession when eventDayId is null && eventSessionId.HasValue:
                RequireUuidV7(eventSessionId.Value, nameof(eventSessionId));
                break;
            default:
                throw new ArgumentException("Admission target scope must contain exactly its required schedule identity.");
        }

        return new AdmissionTarget(id, tenantId, eventId, targetType, eventDayId, eventSessionId);
    }

    internal bool HasSameAuthorityAs(AdmissionTarget other) =>
        other is not null &&
        Id == other.Id &&
        TenantId == other.TenantId &&
        EventId == other.EventId &&
        AdmissionTargetTypeId == other.AdmissionTargetTypeId &&
        ScopeId == other.ScopeId &&
        EventDayId == other.EventDayId &&
        EventSessionId == other.EventSessionId;

    private static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Admission target identity must be an RFC 4122 UUIDv7 value.", parameterName);
        }
    }
}
