using System;

namespace Explore.Application.DTOs.EventCategories;

public sealed record UpdateEventCategoriesDto
{
    public UpdateEventCategoriesEventDto? Event { get; init; }
    public UpdateEventCategoriesCategoryDto? Category { get; init; }
}

public sealed record UpdateEventCategoriesEventDto
{
    public Guid EventId { get; init; }
}

public sealed record UpdateEventCategoriesCategoryDto
{
    public Guid CategoryId { get; init; }
}
