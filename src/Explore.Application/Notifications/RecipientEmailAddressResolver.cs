using Explore.Domain;

namespace Explore.Application.Notifications;

public sealed record RecipientEmailAddressResolution(string? Email, string? SkipReason)
{
    public bool HasVerifiedEmail => Email is not null && SkipReason is null;
}

public static class RecipientEmailAddressResolver
{
    public const string RecipientDeletedOrMissing = "recipient_deleted_or_missing";
    public const string RecipientEmailUnverified = "recipient_email_unverified";
    public const string RecipientEmailMissing = "recipient_email_missing";

    public static RecipientEmailAddressResolution Resolve(User? user, Guid recipientUserId)
    {
        if (user is null || user.Id != recipientUserId || user.IsDeleted)
        {
            return new(null, RecipientDeletedOrMissing);
        }

        string contactAddress = user.Pii?.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        UserIdentityEmailClaim? supported = user.IdentityEmailClaims
            .Where(claim => claim.UserId == recipientUserId
                && claim.Evidence.Any(evidence => evidence.UserId == recipientUserId
                    && evidence.ClaimId == claim.Id && evidence.IsActive))
            .OrderByDescending(claim => string.Equals(claim.NormalizedEmail, contactAddress, StringComparison.Ordinal))
            .ThenBy(claim => claim.Id)
            .FirstOrDefault();
        if (supported is null)
            return new(null, RecipientEmailUnverified);

        string email = supported.NormalizedEmail;
        return string.IsNullOrWhiteSpace(email)
            ? new(null, RecipientEmailMissing)
            : new(email, null);
    }
}
