using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventResourceAudienceRule : ITenantEntity
{
    private Guid _tenantId;

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceAudienceRule));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceAudienceRule));
    }
    public Guid EventId { get; private set; }
    public Guid EventResourceId { get; private set; }
    public int AudienceKindId { get; private set; }
    public Guid? EventSessionId { get; private set; }
    public Guid? EventTicketTypeId { get; private set; }
    public int? AdmissionTargetTypeId { get; private set; }
    public Guid? AdmissionTargetId { get; private set; }
    public bool RequireConfirmedOrder { get; private set; }
    public bool RequireParticipantApproval { get; private set; }
    public bool RequireParticipantCompletion { get; private set; }

    private EventResourceAudienceRule() { }

    public static EventResourceAudienceRule Create(
        Guid tenantId,
        Guid eventId,
        Guid resourceId,
        EventResourceAudienceKindEnum kind,
        Guid? sessionId = null,
        Guid? ticketTypeId = null,
        AdmissionTargetTypeEnum? targetType = null,
        Guid? targetId = null,
        bool? requireConfirmedOrder = null,
        bool requireApproval = false,
        bool requireCompletion = false)
    {
        var rule = new EventResourceAudienceRule
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventId,
            EventResourceId = resourceId,
            AudienceKindId = (int)kind,
            EventSessionId = sessionId,
            EventTicketTypeId = ticketTypeId,
            AdmissionTargetTypeId = (int?)targetType,
            AdmissionTargetId = targetId,
            RequireConfirmedOrder = requireConfirmedOrder ?? kind == EventResourceAudienceKindEnum.SessionRegistrant,
            RequireParticipantApproval = requireApproval,
            RequireParticipantCompletion = requireCompletion
        };
        if (!rule.IsValid())
        {
            throw new ArgumentException("Resource audience ownership or qualifiers are invalid.");
        }
        return rule;
    }

    public bool IsValid()
    {
        if (TenantId == Guid.Empty || EventId == Guid.Empty || EventResourceId == Guid.Empty
            || EventSessionId == Guid.Empty || EventTicketTypeId == Guid.Empty || AdmissionTargetId == Guid.Empty)
        {
            return false;
        }

        var kind = (EventResourceAudienceKindEnum)AudienceKindId;
        if (!Enum.IsDefined(kind)
            || kind != EventResourceAudienceKindEnum.SessionRegistrant
                && (RequireConfirmedOrder || RequireParticipantApproval || RequireParticipantCompletion)
            || kind is not EventResourceAudienceKindEnum.TicketHolder and not EventResourceAudienceKindEnum.CheckedInParticipant
                && EventTicketTypeId.HasValue
            || kind != EventResourceAudienceKindEnum.CheckedInParticipant
                && (AdmissionTargetTypeId.HasValue || AdmissionTargetId.HasValue))
        {
            return false;
        }

        return kind switch
        {
            EventResourceAudienceKindEnum.SessionRegistrant
                or EventResourceAudienceKindEnum.TicketHolder
                or EventResourceAudienceKindEnum.SessionSpeaker => EventSessionId.HasValue,
            EventResourceAudienceKindEnum.CheckedInParticipant => AdmissionTargetId.HasValue
                && AdmissionTargetTypeId switch
                {
                    (int)AdmissionTargetTypeEnum.Event => true,
                    (int)AdmissionTargetTypeEnum.EventDay => true,
                    (int)AdmissionTargetTypeEnum.EventSession => EventSessionId.HasValue,
                    _ => false
                },
            _ => !EventSessionId.HasValue
        };
    }
}
