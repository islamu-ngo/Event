using System;

namespace Explore.Application.DTOs.User;

public sealed record UserTenantRedirectionDto
{
    public Guid? TenantId { get; init; }
    public string? TenantSlug { get; init; }
    public bool HasMultipleTenants { get; init; }
}
