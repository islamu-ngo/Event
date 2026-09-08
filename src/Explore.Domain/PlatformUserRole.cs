using System.ComponentModel.DataAnnotations.Schema;

namespace Explore.Domain;

public class PlatformUserRole
{
    public Guid Id { get; set; }

    [ForeignKey(nameof(User))]
    public Guid UserId { get; set; }
    public required User User { get; set; }

    [ForeignKey(nameof(Role))]
    public int RoleId { get; set; }
    public required Role Role { get; set; }

    public DateTime GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
}
