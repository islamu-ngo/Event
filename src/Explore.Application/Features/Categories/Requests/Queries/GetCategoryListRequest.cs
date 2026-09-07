using System.Collections.Generic;
using Explore.Application.DTOs.Category;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Categories.Requests.Queries;

public sealed record GetCategoryListRequest(
    int PageNumber = 1,
    int PageSize = 20
) : IRequest<PaginatedResult<CategoryListDto>>;
