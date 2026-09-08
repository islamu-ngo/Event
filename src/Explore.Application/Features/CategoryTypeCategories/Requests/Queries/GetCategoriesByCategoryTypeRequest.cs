using Explore.Application.DTOs.Category;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Queries;

public sealed record GetCategoriesByCategoryTypeRequest(int CategoryTypeId = default) : IRequest<List<CategoryListDto>>;
