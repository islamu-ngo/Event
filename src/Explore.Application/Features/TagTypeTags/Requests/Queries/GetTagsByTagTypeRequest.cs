using Explore.Application.DTOs.Tag;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Requests.Queries;

public sealed record GetTagsByTagTypeRequest : IQuery<List<TagListDto>>
{
    public int TagTypeId { get; init; }
}
