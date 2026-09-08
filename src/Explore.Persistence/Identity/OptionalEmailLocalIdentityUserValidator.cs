
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Explore.Persistence.Identity;

public sealed class OptionalEmailLocalIdentityUserValidator : IUserValidator<LocalIdentityUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<LocalIdentityUser> manager, LocalIdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);
        string? email = await manager.GetEmailAsync(user).ConfigureAwait(false);
        if (email is null) return IdentityResult.Success;
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
            return IdentityResult.Failed(manager.ErrorDescriber.InvalidEmail(email));
        LocalIdentityUser? owner = await manager.FindByEmailAsync(email).ConfigureAwait(false);
        return owner is not null && owner.Id != user.Id
            ? IdentityResult.Failed(manager.ErrorDescriber.DuplicateEmail(email))
            : IdentityResult.Success;
    }
}
