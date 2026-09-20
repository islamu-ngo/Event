// Used by the tri-state tag filter dropdown to display tags organized by category.

using Explore.Application.DTOs.TagType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Requests.Queries;

public sealed record GetTagsGroupedByTagTypeRequest : IQuery<List<TagTypeWithTagsDto>>
{
}
