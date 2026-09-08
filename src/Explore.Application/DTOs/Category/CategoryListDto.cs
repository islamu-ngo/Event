using System;

namespace Explore.Application.DTOs.Category;

public sealed record CategoryListDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public required string MasterCode { get; init; }
    public required string FullName { get; init; }
    public Guid? ParentId { get; init; }
    public string? ParentFullName { get; init; }
}
