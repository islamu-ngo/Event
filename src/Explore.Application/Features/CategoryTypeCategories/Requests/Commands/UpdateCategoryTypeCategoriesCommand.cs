using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Commands;

public sealed record UpdateCategoryTypeCategoriesCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid CategoryTypeCategoriesId { get; init; }
    public required UpdateCategoryTypeCategoriesDto CategoryTypeCategoriesDto { get; init; }
}
