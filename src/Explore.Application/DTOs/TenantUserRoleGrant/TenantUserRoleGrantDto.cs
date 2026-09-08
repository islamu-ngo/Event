namespace Explore.Application.DTOs.TenantUserRoleGrant;

public sealed record TenantUserRoleGrantDto
{
    public Guid Id { get; init; }
    public Guid TenantUserId { get; init; }
    public Guid UserId { get; init; }
    public required string UserEmail { get; init; }
    public required string UserFullName { get; init; }
    public Guid TenantId { get; init; }
    public required string TenantFullName { get; init; }
    public int RoleId { get; init; }
    public required string RoleName { get; init; }
    public DateTime GrantedAt { get; init; }
    public Guid? GrantedBy { get; init; }
    public DateTime? RevokedAt { get; init; }
    public Guid? RevokedBy { get; init; }
    public string? RevocationReason { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
