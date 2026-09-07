using Explore.Application.DTOs.TagTypeTags;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Requests.Queries;

public sealed record GetTagTypeTagsListRequest : IRequest<List<TagTypeTagsListDto>>
{
}
