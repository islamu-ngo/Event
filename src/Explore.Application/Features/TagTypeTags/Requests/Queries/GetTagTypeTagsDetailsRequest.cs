using Explore.Application.DTOs.TagTypeTags;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Requests.Queries;

public sealed record GetTagTypeTagsDetailsRequest(Guid Id = default) : IRequest<TagTypeTagsDto>;
