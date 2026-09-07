using Explore.Application.DTOs.TagType;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Requests.Queries;

public sealed record GetTagTypesForTagRequest(Guid TagId = default) : IRequest<List<TagTypeListDto>>;
