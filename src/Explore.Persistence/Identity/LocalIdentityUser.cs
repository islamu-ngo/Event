using Microsoft.AspNetCore.Identity;

namespace Explore.Persistence.Identity;

public sealed class LocalIdentityUser : IdentityUser<Guid>
{
    public LocalIdentityUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.CreateVersion7().ToString("N");
        ConcurrencyStamp = Guid.CreateVersion7().ToString("N");
    }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
