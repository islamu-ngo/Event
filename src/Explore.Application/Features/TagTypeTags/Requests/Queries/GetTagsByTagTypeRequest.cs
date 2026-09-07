using Explore.Application.DTOs.Tag;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Requests.Queries;

public sealed record GetTagsByTagTypeRequest : IRequest<List<TagListDto>>
{
    public int TagTypeId { get; init; }
}
