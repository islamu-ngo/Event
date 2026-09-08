using System;

namespace Explore.Application.DTOs.EventSession;

public sealed record EventSessionGroupAssignmentDto
{
    public Guid EventSessionGroupId { get; init; }

    public required string Name { get; init; }

    public string? Slug { get; init; }

    public string? Color { get; init; }

    public bool IsPrimary { get; init; }

    public int SortOrder { get; init; }
}
