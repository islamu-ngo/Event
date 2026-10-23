using Microsoft.AspNetCore.Identity;

namespace Explore.Persistence.Identity;

internal sealed class LocalIdentityPasswordValidator(LocalPasswordBlocklistSnapshot snapshot)
    : IPasswordValidator<LocalIdentityUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<LocalIdentityUser> manager, LocalIdentityUser user, string? password) =>
        Task.FromResult(snapshot.Allows(password) ? IdentityResult.Success : IdentityResult.Failed(
            new IdentityError { Code = "LocalPasswordPolicyRejected", Description = "The Local password policy rejected this credential." }));
}
