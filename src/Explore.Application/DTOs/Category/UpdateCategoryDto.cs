using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.Category;

public sealed record UpdateCategoryDto
{
    public UpdateCategoryMasterCodeDto? MasterCode { get; init; }
    public UpdateCategoryFullNameDto? FullName { get; init; }
    public UpdateCategoryParentDto? Parent { get; init; }
}

public sealed record UpdateCategoryMasterCodeDto
{
    public required string Value { get; init; }
}

public sealed record UpdateCategoryFullNameDto
{
    public required string Value { get; init; }
}

public sealed record UpdateCategoryParentDto
{
    public OptionalUpdate<Guid?> ParentId { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}
