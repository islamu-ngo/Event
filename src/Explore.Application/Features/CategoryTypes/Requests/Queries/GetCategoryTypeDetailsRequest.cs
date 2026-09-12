using Explore.Application.DTOs.CategoryType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypes.Requests.Queries;

public sealed record GetCategoryTypeDetailsRequest(int Id = default) : IQuery<CategoryTypeDto?>;
