using Explore.Domain.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Explore.Persistence.Identity;

/// <summary>Canonicalizes both sides of native Identity hashing, including login rehash.</summary>
internal sealed class NormalizedLocalIdentityPasswordHasher(IOptions<PasswordHasherOptions> options)
    : IPasswordHasher<LocalIdentityUser>
{
    private readonly PasswordHasher<LocalIdentityUser> _native = new(options);

    public string HashPassword(LocalIdentityUser user, string password)
    {
        if (!LocalPasswordPolicy.TryNormalizeForVerification(password, out string normalized))
            throw new ArgumentException("The Local credential shape is invalid.", nameof(password));
        return _native.HashPassword(user, normalized);
    }

    public PasswordVerificationResult VerifyHashedPassword(LocalIdentityUser user, string hashedPassword, string providedPassword) =>
        LocalPasswordPolicy.TryNormalizeForVerification(providedPassword, out string normalized)
            ? _native.VerifyHashedPassword(user, hashedPassword, normalized)
            : PasswordVerificationResult.Failed;
}
