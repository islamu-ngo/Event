using Microsoft.AspNetCore.Identity;

namespace Explore.Persistence.Identity;

public sealed class LocalIdentityRole : IdentityRole<Guid>
{
    public LocalIdentityRole()
    {
        Id = Guid.CreateVersion7();
        ConcurrencyStamp = Guid.CreateVersion7().ToString("N");
    }

    public LocalIdentityRole(string roleName)
        : this()
    {
        Name = roleName;
        NormalizedName = roleName.ToUpperInvariant();
    }
}
