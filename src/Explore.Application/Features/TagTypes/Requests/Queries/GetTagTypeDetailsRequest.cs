using Explore.Application.DTOs.TagType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypes.Requests.Queries;

public sealed record GetTagTypeDetailsRequest(int Id = default) : IQuery<TagTypeDto?>;
