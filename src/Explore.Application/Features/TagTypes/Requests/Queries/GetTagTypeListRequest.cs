using Explore.Application.DTOs.TagType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypes.Requests.Queries;

public sealed record GetTagTypeListRequest : IQuery<List<TagTypeListDto>>
{
}
