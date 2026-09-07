namespace Explore.Application.DTOs.TenantUserRoleGrant;

public sealed record CreateTenantUserRoleGrantDto
{
    public Guid TenantUserId { get; init; }
    public int RoleId { get; init; }
}
