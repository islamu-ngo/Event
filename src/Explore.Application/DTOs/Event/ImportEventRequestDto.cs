namespace Explore.Application.DTOs.Event;

public sealed record ImportEventRequestDto
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required Guid OwnerActorId { get; init; }
    public required string ProvenanceSource { get; init; }
    public required string ProvenanceExternalId { get; init; }
    public int? EventTypeId { get; init; }
    public int? AudienceGenderId { get; init; }
    public int? AudienceAgeId { get; init; }
    public int? VisibilityTypeId { get; init; }
    public int? EventFormatId { get; init; }
    public string? Timezone { get; init; }
    public required ConfigureEventParticipationDto ParticipationConfiguration { get; init; }
    public Guid? FeaturedImageId { get; init; }
}
