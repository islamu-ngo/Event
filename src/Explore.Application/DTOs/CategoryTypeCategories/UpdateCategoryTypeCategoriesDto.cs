namespace Explore.Application.DTOs.CategoryTypeCategories;

public sealed record UpdateCategoryTypeCategoriesDto
{
    public UpdateCategoryTypeCategoriesRelationshipDto? Relationship { get; init; }
}

public sealed record UpdateCategoryTypeCategoriesRelationshipDto
{
    public Guid? CategoryId { get; init; }
    public int? CategoryTypeId { get; init; }
}
