namespace Explore.Application.DTOs.EventOrganizerClaim;

public sealed record SubmitEventOrganizerClaimDto
{
    public Guid ClaimantActorId { get; init; }
    public required string EvidenceType { get; init; }
    public required string EvidenceReference { get; init; }
}
