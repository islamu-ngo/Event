using System;
using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.Group;

public sealed record UpdateGroupDto
{
    public UpdateGroupFullNameDto? FullName { get; init; }
    public UpdateGroupDescriptionDto? Description { get; init; }
    public UpdateGroupParentOrganizationDto? ParentOrganization { get; init; }
    public UpdateGroupParentGroupDto? ParentGroup { get; init; }
}

public sealed record UpdateGroupFullNameDto
{
    public required string Value { get; init; }
}

public sealed record UpdateGroupDescriptionDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateGroupParentOrganizationDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}

public sealed record UpdateGroupParentGroupDto
{
    public OptionalUpdate<Guid?> Value { get; init; } = OptionalUpdate<Guid?>.Unspecified();
}
