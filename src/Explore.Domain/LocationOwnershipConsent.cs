namespace Explore.Domain;

public sealed record LocationOwnershipConsent(
    Guid NewOwnerUserId,
    Guid ConsentedByUserId,
    DateTime ConsentedAtUtc,
    string ConsentVersion);
