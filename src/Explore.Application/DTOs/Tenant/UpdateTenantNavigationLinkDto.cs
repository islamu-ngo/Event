using System;
using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.Tenant;

/// <summary>
/// DTO for partially updating an existing tenant navigation link.
/// </summary>
public sealed record UpdateTenantNavigationLinkDto
{
    public UpdateTenantNavigationLinkLabelDto? Label { get; init; }
    public UpdateTenantNavigationLinkUrlDto? Url { get; init; }
    public UpdateTenantNavigationLinkIconDto? Icon { get; init; }
    public UpdateTenantNavigationLinkOpenInNewTabDto? OpenInNewTab { get; init; }
}

public sealed record UpdateTenantNavigationLinkLabelDto
{
    public required string Value { get; init; }
}

public sealed record UpdateTenantNavigationLinkUrlDto
{
    public required string Value { get; init; }
}

public sealed record UpdateTenantNavigationLinkIconDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateTenantNavigationLinkOpenInNewTabDto
{
    public bool? Value { get; init; }
}
