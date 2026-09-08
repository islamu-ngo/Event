using System.Collections.Generic;
using Explore.Application.DTOs.Tag;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tags.Requests.Queries;

public sealed record GetTagListRequest(
    int PageNumber = 1,
    int PageSize = 20
) : IRequest<PaginatedResult<TagListDto>>;
