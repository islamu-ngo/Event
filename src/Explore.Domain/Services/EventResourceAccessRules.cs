using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Domain.Services;

public static class EventResourceAccessRules
{
    public static EventResourceAccessDecision Evaluate(
        EventResource resource, EventResourceAccessFacts facts, DateTimeOffset nowUtc) =>
        Evaluate(EventResourcePolicySnapshot.Capture(resource), facts, nowUtc);

    public static EventResourceAccessDecision Evaluate(
        EventResourcePolicySnapshot resource, EventResourceAccessFacts facts, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(facts);
        if (resource.IsDeleted || resource.TenantId != facts.TenantId
            || resource.PublicationStateId != (int)EventResourcePublicationStateEnum.Published
            || !IsParentEligible(resource, facts.Parent)
            || !Enum.IsDefined((EventResourceDisclosureModeEnum)resource.DisclosureModeId)
            || resource.AudienceRules.Count == 0
            || resource.AudienceRules.Any(rule => !rule.IsValid || rule.TenantId != resource.TenantId
                || rule.EventId != resource.EventId || rule.EventResourceId != resource.Id
                || resource.EventSessionId.HasValue && rule.EventSessionId.HasValue && rule.EventSessionId != resource.EventSessionId)
            || resource.AudienceRules.Count > 1 && resource.AudienceRules.Any(rule =>
                rule.AudienceKindId == (int)EventResourceAudienceKindEnum.Public))
        {
            return new(false, false, false);
        }

        bool eligible = resource.Availability.IsAvailableAt(nowUtc, facts.Parent.Schedule)
            && resource.AudienceRules.Any(rule => Matches(rule, facts, nowUtc));
        bool publicMetadata = resource.DisclosureModeId is (int)EventResourceDisclosureModeEnum.Teaser
            or (int)EventResourceDisclosureModeEnum.Public && !string.IsNullOrWhiteSpace(resource.PublicTitle);
        return new(eligible || publicMetadata, eligible,
            eligible && facts.PayloadSafetySatisfied && resource.HasPublishablePayload);
    }

    public static bool IsParentEligible(EventResource resource, EventResourceParentFacts parent) =>
        IsParentEligible(EventResourcePolicySnapshot.Capture(resource), parent);

    public static bool IsParentEligible(EventResourcePolicySnapshot resource, EventResourceParentFacts parent) =>
        parent.TenantId == resource.TenantId && parent.EventId == resource.EventId
        && !parent.EventDeleted && parent.EventEligible && parent.EventStatus == EventStatusEnum.Published
        && parent.EventSessionId == resource.EventSessionId
        && (resource.EventSessionId.HasValue
            ? !parent.SessionDeleted
                && parent.SessionStatus is EventSessionStatusEnum.Published or EventSessionStatusEnum.Completed
            : resource.Availability.StartAnchor is not EventResourceAvailabilityAnchorEnum.SessionStart
                and not EventResourceAvailabilityAnchorEnum.SessionEnd
                && resource.Availability.EndAnchor is not EventResourceAvailabilityAnchorEnum.SessionStart
                    and not EventResourceAvailabilityAnchorEnum.SessionEnd);

    private static bool Matches(EventResourceAudiencePolicySnapshot rule, EventResourceAccessFacts facts, DateTimeOffset nowUtc)
    {
        var kind = (EventResourceAudienceKindEnum)rule.AudienceKindId;
        if (kind == EventResourceAudienceKindEnum.Public)
        {
            return true;
        }
        if (facts.IsMachineCaller || facts.SubjectUserId is null || facts.SubjectUserId == Guid.Empty)
        {
            return false;
        }

        return facts.Audience.Any(fact =>
            fact.IsCurrent && fact.SubjectUserId == facts.SubjectUserId
            && fact.TenantId == rule.TenantId && fact.EventId == rule.EventId && fact.Kind == kind
            && (!fact.ValidFromUtc.HasValue || nowUtc >= fact.ValidFromUtc)
            && (!fact.ExpiresAtUtc.HasValue || nowUtc < fact.ExpiresAtUtc)
            && (!rule.EventSessionId.HasValue || fact.EventSessionId == rule.EventSessionId)
            && (!rule.EventTicketTypeId.HasValue || fact.EventTicketTypeId == rule.EventTicketTypeId)
            && (!rule.RequireConfirmedOrder || fact.OrderConfirmed)
            && (!rule.RequireParticipantApproval || fact.ApprovedSubjectUserId == facts.SubjectUserId)
            && (!rule.RequireParticipantCompletion || fact.CompletedSubjectUserId == facts.SubjectUserId)
            && (kind != EventResourceAudienceKindEnum.AnyEventSessionSpeaker || fact.EventSessionId.HasValue)
            && (kind != EventResourceAudienceKindEnum.CheckedInParticipant
                || (int?)fact.AdmissionTargetType == rule.AdmissionTargetTypeId && fact.AdmissionTargetId == rule.AdmissionTargetId));
    }
}

/// <summary>A capture-only projection: neither aggregate mutations nor collection aliases can change an evaluation.</summary>
public sealed class EventResourcePolicySnapshot
{
    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid EventId { get; }
    public Guid? EventSessionId { get; }
    public int PublicationStateId { get; }
    public int DisclosureModeId { get; }
    public int EventResourceKindId { get; }
    public int EventResourceDeliveryTypeId { get; }
    public bool IsDeleted { get; }
    public string? PublicTitle { get; }
    public bool HasPublishablePayload { get; }
    public EventResourceAvailability Availability { get; }
    public IReadOnlyList<EventResourceAudiencePolicySnapshot> AudienceRules { get; }

    private EventResourcePolicySnapshot(EventResource resource)
    {
        Id = resource.Id;
        TenantId = resource.TenantId;
        EventId = resource.EventId;
        EventSessionId = resource.EventSessionId;
        PublicationStateId = resource.PublicationStateId;
        DisclosureModeId = resource.DisclosureModeId;
        EventResourceKindId = resource.EventResourceKindId;
        EventResourceDeliveryTypeId = resource.EventResourceDeliveryTypeId;
        IsDeleted = resource.IsDeleted;
        PublicTitle = resource.PublicTitle;
        HasPublishablePayload = resource.HasPublishablePayload();
        Availability = resource.Availability;
        AudienceRules = Array.AsReadOnly(resource.AudienceRules.Select(rule => new EventResourceAudiencePolicySnapshot(
            rule.TenantId, rule.EventId, rule.EventResourceId, rule.AudienceKindId, rule.EventSessionId,
            rule.EventTicketTypeId, rule.AdmissionTargetTypeId, rule.AdmissionTargetId,
            rule.RequireConfirmedOrder, rule.RequireParticipantApproval, rule.RequireParticipantCompletion,
            rule.IsValid())).ToArray());
    }

    public static EventResourcePolicySnapshot Capture(EventResource resource) => new(resource);
}

public sealed record EventResourceAudiencePolicySnapshot(
    Guid TenantId, Guid EventId, Guid EventResourceId, int AudienceKindId, Guid? EventSessionId,
    Guid? EventTicketTypeId, int? AdmissionTargetTypeId, Guid? AdmissionTargetId,
    bool RequireConfirmedOrder, bool RequireParticipantApproval, bool RequireParticipantCompletion, bool IsValid);

public sealed record EventResourceParentFacts(
    Guid TenantId,
    Guid EventId,
    Guid? EventSessionId,
    EventStatusEnum EventStatus,
    bool EventDeleted,
    bool EventEligible,
    EventSessionStatusEnum? SessionStatus,
    bool SessionDeleted,
    EventResourceScheduleFacts Schedule);

public sealed record EventResourceAudienceFact
{
    public required Guid TenantId { get; init; }
    public required Guid EventId { get; init; }
    public required Guid SubjectUserId { get; init; }
    public required EventResourceAudienceKindEnum Kind { get; init; }
    public required bool IsCurrent { get; init; }
    public Guid? EventSessionId { get; init; }
    public Guid? EventTicketTypeId { get; init; }
    public AdmissionTargetTypeEnum? AdmissionTargetType { get; init; }
    public Guid? AdmissionTargetId { get; init; }
    public bool OrderConfirmed { get; init; }
    public Guid? ApprovedSubjectUserId { get; init; }
    public Guid? CompletedSubjectUserId { get; init; }
    public DateTimeOffset? ValidFromUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record EventResourceAccessFacts
{
    public Guid TenantId { get; }
    public Guid? SubjectUserId { get; }
    public bool IsMachineCaller { get; }
    public EventResourceParentFacts Parent { get; }
    public IReadOnlyList<EventResourceAudienceFact> Audience { get; }
    public bool PayloadSafetySatisfied { get; }

    public EventResourceAccessFacts(Guid tenantId, Guid? subjectUserId, bool isMachineCaller,
        EventResourceParentFacts parent, IEnumerable<EventResourceAudienceFact> audience,
        bool payloadSafetySatisfied)
    {
        TenantId = tenantId;
        SubjectUserId = subjectUserId;
        IsMachineCaller = isMachineCaller;
        Parent = parent;
        Audience = Array.AsReadOnly(audience.ToArray());
        PayloadSafetySatisfied = payloadSafetySatisfied;
    }
}

public sealed record EventResourceAccessDecision(
    bool DiscloseMetadata, bool DisclosePrivateMetadata, bool CanAccess);
