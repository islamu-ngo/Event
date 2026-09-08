namespace Explore.Domain;

public sealed record VerifiedPurchaserIdentity(string Kind, string Value)
{
    public static VerifiedPurchaserIdentity Account(Guid accountUserId)
    {
        if (accountUserId == Guid.Empty)
        {
            throw new ArgumentException("Account user id is required.", nameof(accountUserId));
        }

        return new VerifiedPurchaserIdentity(nameof(Account), accountUserId.ToString("D"));
    }

    public static VerifiedPurchaserIdentity Email(string normalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ArgumentException("Verified normalized email is required.", nameof(normalizedEmail));
        }

        return new VerifiedPurchaserIdentity(nameof(Email), normalizedEmail.Trim().ToUpperInvariant());
    }

    public static VerifiedPurchaserIdentity Actor(Guid purchaserActorId)
    {
        if (purchaserActorId == Guid.Empty)
        {
            throw new ArgumentException("Purchaser actor id is required.", nameof(purchaserActorId));
        }

        return new VerifiedPurchaserIdentity(nameof(Actor), purchaserActorId.ToString("D"));
    }
}
