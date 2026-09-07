using Explore.Application.DTOs.TagType;
using MediatR;

namespace Explore.Application.Features.TagTypes.Requests.Queries;

public sealed record GetTagTypeDetailsRequest(int Id = default) : IRequest<TagTypeDto>;
