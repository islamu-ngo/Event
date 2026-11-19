using Explore.Domain;

namespace Explore.Application.Notifications;

public sealed record RecipientEmailAddressResolution(string? Email, string? SkipReason)
{
    public bool HasVerifiedEmail => Email is not null && SkipReason is null;
}

public sealed record EmailRecipientClaim(Guid Id, string NormalizedEmail);

/// <summary>
/// Function-mediated recipient projection: exactly the user status, contact address and
/// evidence-verified email claims required to resolve a dispatch address. PII stays behind
/// the native owner function; ordinary runtime connectors never read the tables directly.
/// </summary>
public sealed record EmailRecipient(
    Guid Id,
    bool IsDeleted,
    string? ContactEmail,
    IReadOnlyList<EmailRecipientClaim> VerifiedEmailClaims);

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

    /// <summary>
    /// Resolves a dispatch address from the function-mediated recipient projection.
    /// Claims must already be filtered to those with active evidence; ranking mirrors
    /// the entity overload so both paths select the same address.
    /// </summary>
    public static RecipientEmailAddressResolution Resolve(EmailRecipient? recipient, Guid recipientUserId)
    {
        if (recipient is null || recipient.Id != recipientUserId || recipient.IsDeleted)
        {
            return new(null, RecipientDeletedOrMissing);
        }

        string contactAddress = recipient.ContactEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        EmailRecipientClaim? supported = recipient.VerifiedEmailClaims
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
