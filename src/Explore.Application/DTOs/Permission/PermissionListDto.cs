namespace Explore.Application.DTOs.Permission;

public sealed record PermissionListDto
{
    public int Id { get; init; }
    public required string MasterCode { get; init; }
    public required string FullName { get; init; }
    public required string ResourceKind { get; init; }
    public required string Action { get; init; }
    public required string GroupName { get; init; }
    public int RoleScopeId { get; init; }
    public required string RoleScopeCode { get; init; }
    public required string RoleScopeName { get; init; }
}
