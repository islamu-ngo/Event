using Explore.Application.DTOs.CategoryType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypes.Requests.Queries;

public sealed record GetCategoryTypeListRequest : IQuery<List<CategoryTypeListDto>>
{
}
