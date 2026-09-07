using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Operations;

public enum EmergencyAdminProvisioningOutcome
{
    Granted,
    Reassigned,
    AlreadyPresent,
    TargetNotFound,
    InvalidRoleAuthority,
}

public sealed class EmergencyAdminProvisioningOperation(
    ExploreDbContext dbContext)
{
    public Task<EmergencyAdminProvisioningOutcome> GrantAsync(
        AtprotoDid did,
        CancellationToken cancellationToken,
        bool revokeOtherAdministrators = false) =>
        new EfCoreUnitOfWork(dbContext).ExecuteBootstrapConvergenceAsync(
            transactionToken => GrantInCurrentTransactionAsync(
                did,
                revokeOtherAdministrators,
                transactionToken),
            cancellationToken);

    private async Task<EmergencyAdminProvisioningOutcome>
        GrantInCurrentTransactionAsync(
            AtprotoDid did,
            bool revokeOtherAdministrators,
            CancellationToken cancellationToken)
    {
        UserExternalLogin? login = await dbContext.UserExternalLogins
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.AuthenticationProviderId
                        == (int)AuthenticationProviderKind.Atproto
                    && candidate.ProviderKey == did.Value,
                cancellationToken);
        if (login is null)
        {
            return EmergencyAdminProvisioningOutcome.TargetNotFound;
        }

        bool activeUser = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == login.UserId,
                cancellationToken);
        if (!activeUser)
        {
            return EmergencyAdminProvisioningOutcome.TargetNotFound;
        }

        Role? role = await dbContext.Roles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == (int)RoleEnum.Admin
                    && candidate.MasterCode == "platform.admin"
                    && candidate.RoleScopeId
                        == (int)RoleScopeEnum.Platform,
                cancellationToken);
        if (role is null)
        {
            return EmergencyAdminProvisioningOutcome.InvalidRoleAuthority;
        }

        bool alreadyPresent = await dbContext.PlatformUserRoles
            .AsNoTracking()
            .AnyAsync(
                grant =>
                    grant.UserId == login.UserId
                    && grant.RoleId == role.Id,
                cancellationToken);
        bool reassigned = false;
        if (revokeOtherAdministrators)
        {
            List<PlatformUserRole> otherAdministrators =
                await dbContext.PlatformUserRoles
                    .Where(grant =>
                        grant.RoleId == role.Id
                        && grant.UserId != login.UserId)
                    .ToListAsync(cancellationToken);
            if (otherAdministrators.Count != 0)
            {
                dbContext.PlatformUserRoles.RemoveRange(
                    otherAdministrators);
                reassigned = true;
            }
        }

        if (!alreadyPresent)
        {
            dbContext.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = login.UserId,
                User = null!,
                RoleId = role.Id,
                Role = null!,
                GrantedAt = DateTime.UtcNow,
                GrantedBy = null,
            });
        }

        if (alreadyPresent && !reassigned)
        {
            return EmergencyAdminProvisioningOutcome.AlreadyPresent;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return reassigned
            ? EmergencyAdminProvisioningOutcome.Reassigned
            : EmergencyAdminProvisioningOutcome.Granted;
    }
}
