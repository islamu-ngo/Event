namespace Explore.Application.DTOs.EventRoleAssignment;

public sealed record EventRolePresetDto
{
    public int RoleId { get; init; }
    public required string MasterCode { get; init; }
    public required string FullName { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyCollection<string> PermissionCodes { get; init; }
}
