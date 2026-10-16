using Explore.Domain.Enums;

namespace Explore.Domain;

/// <summary>Policy facts needed for governance correction, without physical address or location payloads.</summary>
public sealed record EventLocationGovernanceState(
    Guid TenantId,
    Guid EventId,
    Guid EventLocationId,
    bool IsDeleted,
    bool IsToBeAnnounced,
    int? LocationKindId,
    EventLocationDisclosureFields Fields,
    LocationDisclosureAudienceEnum Audience,
    DateTime? RevealFullDetailsFromUtc,
    DateTime CreatedAt,
    int PolicyVersion,
    Guid ConcurrencyStamp)
{
    public EventLocationDisclosureAudit CreateTighteningAudit(Guid actorUserId, DateTime changedAtUtc)
    {
        if (IsDeleted)
            throw new InvalidOperationException("A detached EventLocation disclosure policy cannot be changed.");
        return EventLocationDisclosureAudit.Create(
            TenantId, EventLocationId, actorUserId, Fields, Fields, Audience, Audience,
            RevealFullDetailsFromUtc, RevealFullDetailsFromUtc, PolicyVersion, PolicyVersion + 1,
            EventLocationDisclosureAuditReasonEnum.GovernanceTightening, changedAtUtc);
    }
}
