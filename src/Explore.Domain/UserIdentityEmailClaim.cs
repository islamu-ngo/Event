namespace Explore.Domain;

public sealed class UserIdentityEmailClaim
{
    private UserIdentityEmailClaim()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string NormalizedEmail { get; private set; } = string.Empty;
    public ICollection<UserIdentityEmailEvidence> Evidence { get; set; } = [];

    public static UserIdentityEmailClaim Create(Guid userId, string normalizedEmail, Guid? id = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("An identity email claim requires an account owner.", nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        if (normalizedEmail.Length > 320
            || !string.Equals(normalizedEmail, normalizedEmail.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("The identity address must already be normalized.", nameof(normalizedEmail));

        return new UserIdentityEmailClaim
        {
            Id = id ?? Guid.CreateVersion7(),
            UserId = userId,
            NormalizedEmail = normalizedEmail
        };
    }
}
