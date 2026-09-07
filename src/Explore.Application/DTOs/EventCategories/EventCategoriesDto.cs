using System;

namespace Explore.Application.DTOs.EventCategories;

public sealed record EventCategoriesDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public Guid EventId { get; init; }
    public string? EventTitle { get; init; }
    public Guid CategoryId { get; init; }
    public string? CategoryFullName { get; init; }
    public string? CategoryMasterCode { get; init; } // For i18n with Tolgee
    public Guid TenantId { get; init; }
}
