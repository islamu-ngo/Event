using Explore.Application.DTOs.TenantSettings;

namespace Explore.Application.DTOs.Tenant;

public sealed record CreateTenantDto
{
    public required string FullName { get; init; }
    public required string Slug { get; init; }
    public bool IsActive { get; init; }
    public TenantDirectoryOperatorIdentityInputDto? DirectoryOperatorIdentity { get; init; }
    /// <summary>
    /// When true, the requesting user is automatically added as a tenant administrator of the newly created tenant.
    /// Has no effect if the request is unauthenticated or the tenant.admin role cannot be resolved.
    /// </summary>
    public bool AssignCurrentUserAsTenantAdmin { get; init; }
}
