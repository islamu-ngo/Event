// Used by GetTagsGroupedByTagTypeRequest to return tags organized by category.

using Explore.Application.DTOs.Tag;

namespace Explore.Application.DTOs.TagType;

public sealed record TagTypeWithTagsDto
{
    private IReadOnlyList<TagListDto> _tags = Array.AsReadOnly(Array.Empty<TagListDto>());

    public int Id { get; init; }
    public required string FullName { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<TagListDto> Tags
    {
        get => _tags;
        init => _tags = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}
