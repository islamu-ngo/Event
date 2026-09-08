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

        if (user.EmailVerified != true)
        {
            return new(null, RecipientEmailUnverified);
        }

        string email = user.Pii?.Email?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(email)
            ? new(null, RecipientEmailMissing)
            : new(email, null);
    }
}
