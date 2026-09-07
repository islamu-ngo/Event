using Explore.Application.DTOs.TagType;
using MediatR;

namespace Explore.Application.Features.TagTypes.Requests.Queries;

public sealed record GetTagTypeListRequest : IRequest<List<TagTypeListDto>>
{
}
