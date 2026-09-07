namespace Explore.Domain;

public class RolePermission
{
    public int RoleId { get; set; }
    public required Role Role { get; set; }

    public int PermissionId { get; set; }
    public required Permission Permission { get; set; }

    public DateTime GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
}
