using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Requests.Queries;

public sealed record GetTagTypeTagsDetailsRequest(Guid Id = default) : IQuery<TagTypeTagsDto?>;
