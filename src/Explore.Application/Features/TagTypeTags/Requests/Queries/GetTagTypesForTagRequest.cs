using Explore.Application.DTOs.TagType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Requests.Queries;

public sealed record GetTagTypesForTagRequest(Guid TagId = default) : IQuery<List<TagTypeListDto>>;
