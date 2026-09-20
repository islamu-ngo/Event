using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

public static partial class EventMapper
{
    public static EventOrganizerClaimDto ToDetail(EventOrganizerClaim source) => MapClaim(source) with
    {
        ClaimantActorDisplayName = source.ClaimantActor?.Pii?.DisplayName,
        ClaimantActorUserId = source.ClaimantActor?.UserId,
        ClaimantActorOrganizationId = source.ClaimantActor?.OrganizationId,
        ClaimantActorGroupId = source.ClaimantActor?.GroupId,
        StatusCode = source.Status?.MasterCode,
        StatusName = source.Status?.FullName,
        EventActorId = source.Event?.ActorId ?? Guid.Empty,
        EventActorUserId = source.Event?.Actor?.UserId,
        EventActorOrganizationId = source.Event?.Actor?.OrganizationId,
        EventActorGroupId = source.Event?.Actor?.GroupId,
        EventProvenanceTypeId = source.Event?.EventProvenanceTypeId ?? 0,
        EventProvenanceTypeCode = source.Event?.EventProvenanceType?.MasterCode,
        EventOrganizerActorId = source.Event?.OrganizerActorId,
        EventSubmittedByUserId = source.Event?.SubmittedByUserId
    };

    // Authority scalars are selected above; never traverse claimant/event/reviewer or tenant graphs.
    // Audit fields other than CreatedAt and the concurrency stamp are not part of this response.
    [MapperIgnoreSource(nameof(EventOrganizerClaim.ClaimantActor))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.Event))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.Status))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.Tenant))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.ReviewerUser))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.CreatedBy))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.IsDeleted))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.DeletedAt))]
    [MapperIgnoreSource(nameof(EventOrganizerClaim.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.ClaimantActorDisplayName))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.ClaimantActorUserId))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.ClaimantActorOrganizationId))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.ClaimantActorGroupId))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.StatusCode))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.StatusName))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.EventActorId))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.EventActorUserId))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.EventActorOrganizationId))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.EventActorGroupId))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.EventProvenanceTypeId))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.EventProvenanceTypeCode))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.EventOrganizerActorId))]
    [MapperIgnoreTarget(nameof(EventOrganizerClaimDto.EventSubmittedByUserId))]
    private static partial EventOrganizerClaimDto MapClaim(EventOrganizerClaim source);
}
