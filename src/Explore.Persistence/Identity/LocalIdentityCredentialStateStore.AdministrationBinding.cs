using Explore.Application.Contracts.Identity;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    private async Task<BaseCommandResponse<Guid>> BindAdministrationAsync(
        LocalCredentialProvisioningSnapshot snapshot, CancellationToken token)
    {
        var receipt = snapshot.Receipt;
        string providerKey = receipt.LocalSubjectId.ToString("D");
        var user = await applicationDbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receipt.ApplicationUserId, token);
        var actor = await applicationDbContext.Actors.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receipt.PersonalActorId, token);
        var login = await applicationDbContext.UserExternalLogins.AsNoTracking()
            .SingleOrDefaultAsync(row => row.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
                && row.ProviderKey == providerKey, token);
        if (user is { IsDeleted: false } && actor is { IsDeleted: false, IsSuspended: false }
            && actor.ActorTypeId == (int)ActorTypeEnum.User && actor.UserId == receipt.ApplicationUserId
            && login is not null && login.Id == receipt.ExternalLoginId && login.UserId == receipt.ApplicationUserId)
            return BaseCommandResponse.Success(id: receipt.OperationId);
        if (user is not null || actor is not null || login is not null
            || await applicationDbContext.Actors.AnyAsync(row => row.UserId == receipt.ApplicationUserId, token)
            || await applicationDbContext.UserExternalLogins.AnyAsync(row => row.Id == receipt.ExternalLoginId
                || row.UserId == receipt.ApplicationUserId, token))
            return BaseCommandResponse.Conflict(id: receipt.OperationId);
        if (snapshot.Email is null)
            return BaseCommandResponse.Conflict(id: receipt.OperationId);

        var created = new User
        {
            Id = receipt.ApplicationUserId,
            Pii = new UserPii { Email = snapshot.Email, FirstName = snapshot.FirstName, LastName = snapshot.LastName },
            EmailVerified = snapshot.EmailVerified,
            CreatedAt = receipt.CreatedAt,
            CreatedBy = receipt.InitiatingApplicationUserId
        };
        applicationDbContext.Users.Add(created);
        await applicationDbContext.SaveChangesAsync(token);
        applicationDbContext.Actors.Add(new Actor
        {
            Id = receipt.PersonalActorId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = receipt.ApplicationUserId,
            User = created,
            Pii = new ActorPii { DisplayName = $"{snapshot.FirstName} {snapshot.LastName}".Trim() },
            CreatedAt = receipt.CreatedAt,
            CreatedBy = receipt.InitiatingApplicationUserId
        });
        await applicationDbContext.SaveChangesAsync(token);
        applicationDbContext.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = receipt.ExternalLoginId,
            UserId = receipt.ApplicationUserId,
            User = created,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = providerKey,
            ProviderDisplayName = "Local",
            CreatedAt = receipt.CreatedAt,
            CreatedBy = receipt.InitiatingApplicationUserId
        });
        await applicationDbContext.SaveChangesAsync(token);
        return BaseCommandResponse.Success(id: receipt.OperationId);
    }
}
