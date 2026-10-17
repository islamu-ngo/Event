using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>Seeds persisted administrator rows without constructing authenticated mutation services.</summary>
internal static class InstanceSettingsCommandFixture
{
    internal static async Task<Guid> SeedAdministratorAsync(ExploreDbContext context)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"settings-admin-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Settings",
                LastName = "Administrator"
            },
            CreatedAt = DateTime.UtcNow
        };
        var role = await context.Roles.SingleAsync(role => role.MasterCode == "platform.admin");
        await new PlatformUserRoleRepository(context).Create(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            RoleId = role.Id,
            Role = role,
            GrantedAt = DateTime.UtcNow
        });
        return user.Id;
    }
}
