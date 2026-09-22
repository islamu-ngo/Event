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
    public Guid? ResourceEventSessionId { get; private set; }
    public Guid ResourceSessionScopeId { get; private set; }
    public Guid? EventSessionId { get; private set; }
    public Guid? EventTicketCatalogVersionId { get; private set; }
    public Guid? EventTicketTypeId { get; private set; }
    public int? AdmissionTargetTypeId { get; private set; }
    public Guid? AdmissionTargetId { get; private set; }
    public Guid? AdmissionTargetScopeId { get; private set; }
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
        bool requireCompletion = false,
        Guid? ticketCatalogVersionId = null,
        Guid? targetScopeId = null)
    {
        Guid? resolvedTargetScopeId = targetType switch
        {
            AdmissionTargetTypeEnum.Event => eventId,
            AdmissionTargetTypeEnum.EventSession => sessionId,
            AdmissionTargetTypeEnum.EventDay => targetScopeId,
            _ => null
        };
        if (targetScopeId.HasValue && targetScopeId != resolvedTargetScopeId)
        {
            throw new ArgumentException("Admission target scope contradicts the audience qualifier.", nameof(targetScopeId));
        }
        var rule = new EventResourceAudienceRule
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventId,
            EventResourceId = resourceId,
            AudienceKindId = (int)kind,
            EventSessionId = sessionId,
            EventTicketCatalogVersionId = ticketCatalogVersionId,
            EventTicketTypeId = ticketTypeId,
            AdmissionTargetTypeId = (int?)targetType,
            AdmissionTargetId = targetId,
            AdmissionTargetScopeId = resolvedTargetScopeId,
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
            || ResourceEventSessionId == Guid.Empty || EventSessionId == Guid.Empty
            || EventTicketCatalogVersionId == Guid.Empty || EventTicketTypeId == Guid.Empty
            || AdmissionTargetId == Guid.Empty || AdmissionTargetScopeId == Guid.Empty)
        {
            return false;
        }

        var kind = (EventResourceAudienceKindEnum)AudienceKindId;
        if (!Enum.IsDefined(kind)
            || kind != EventResourceAudienceKindEnum.SessionRegistrant
                && (RequireConfirmedOrder || RequireParticipantApproval || RequireParticipantCompletion)
            || kind is not EventResourceAudienceKindEnum.TicketHolder and not EventResourceAudienceKindEnum.CheckedInParticipant
                && (EventTicketCatalogVersionId.HasValue || EventTicketTypeId.HasValue)
            || EventTicketCatalogVersionId.HasValue != EventTicketTypeId.HasValue
            || kind != EventResourceAudienceKindEnum.CheckedInParticipant
                && (AdmissionTargetTypeId.HasValue || AdmissionTargetId.HasValue || AdmissionTargetScopeId.HasValue))
        {
            return false;
        }

        return kind switch
        {
            EventResourceAudienceKindEnum.SessionRegistrant
                or EventResourceAudienceKindEnum.TicketHolder
                or EventResourceAudienceKindEnum.SessionSpeaker => EventSessionId.HasValue,
            EventResourceAudienceKindEnum.CheckedInParticipant => AdmissionTargetId.HasValue && AdmissionTargetScopeId.HasValue
                && AdmissionTargetTypeId switch
                {
                    (int)AdmissionTargetTypeEnum.Event => AdmissionTargetScopeId == EventId,
                    (int)AdmissionTargetTypeEnum.EventDay => true,
                    (int)AdmissionTargetTypeEnum.EventSession => EventSessionId.HasValue && AdmissionTargetScopeId == EventSessionId,
                    _ => false
                },
            _ => !EventSessionId.HasValue
        };
    }

    internal void BindResourceSession(Guid? resourceEventSessionId, Guid resourceSessionScopeId)
    {
        if (ResourceSessionScopeId != Guid.Empty
            && (ResourceSessionScopeId != resourceSessionScopeId || ResourceEventSessionId != resourceEventSessionId))
        {
            throw new InvalidOperationException("Resource audience ownership cannot be rebound.");
        }
        if (resourceSessionScopeId == Guid.Empty || resourceEventSessionId == Guid.Empty
            || resourceEventSessionId.HasValue && EventSessionId.HasValue && resourceEventSessionId != EventSessionId)
        {
            throw new ArgumentException("A session-owned resource cannot select a sibling session.");
        }
        ResourceEventSessionId = resourceEventSessionId;
        ResourceSessionScopeId = resourceSessionScopeId;
    }
}
