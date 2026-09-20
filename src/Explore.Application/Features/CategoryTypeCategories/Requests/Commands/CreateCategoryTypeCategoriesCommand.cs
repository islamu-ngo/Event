using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Commands;

public sealed record CreateCategoryTypeCategoriesCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required CreateCategoryTypeCategoriesDto CategoryTypeCategoriesDto { get; init; }
}
