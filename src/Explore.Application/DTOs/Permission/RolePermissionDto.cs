namespace Explore.Application.DTOs.Permission;

public sealed record RolePermissionDto
{
    public int RoleId { get; init; }
    public required string RoleName { get; init; }
    public int PermissionId { get; init; }
    public required string PermissionMasterCode { get; init; }
    public required string PermissionFullName { get; init; }
    public required string ResourceKind { get; init; }
    public required string Action { get; init; }
}
