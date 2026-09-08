namespace Explore.Application.DTOs.TagTypeTags;

public sealed record UpdateTagTypeTagsDto
{
    public UpdateTagTypeTagsRelationshipDto? Relationship { get; init; }
}

public sealed record UpdateTagTypeTagsRelationshipDto
{
    public Guid? TagId { get; init; }
    public int? TagTypeId { get; init; }
}
