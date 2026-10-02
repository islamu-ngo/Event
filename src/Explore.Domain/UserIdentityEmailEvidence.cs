namespace Explore.Domain;

public sealed class UserIdentityEmailEvidence
{
    private UserIdentityEmailEvidence()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ClaimId { get; private set; }
    public Guid ExternalLoginId { get; private set; }
    public DateTime ObservedAt { get; private set; }
    public bool IsActive { get; private set; }

    public static UserIdentityEmailEvidence Create(
        Guid userId, Guid claimId, Guid externalLoginId, DateTime observedAt, Guid? id = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("Verification evidence requires an account owner.", nameof(userId));
        if (claimId == Guid.Empty)
            throw new ArgumentException("Verification evidence requires an identity email claim.", nameof(claimId));
        if (externalLoginId == Guid.Empty)
            throw new ArgumentException("Verification evidence requires an exact account binding.", nameof(externalLoginId));
        if (observedAt == default || observedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Verification evidence requires a UTC observation.", nameof(observedAt));

        return new UserIdentityEmailEvidence
        {
            Id = id ?? Guid.CreateVersion7(),
            UserId = userId,
            ClaimId = claimId,
            ExternalLoginId = externalLoginId,
            ObservedAt = observedAt,
            IsActive = true
        };
    }

    public void Invalidate()
    {
        IsActive = false;
    }
}
