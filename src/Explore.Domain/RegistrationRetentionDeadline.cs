using Explore.Domain.Enums;

namespace Explore.Domain;

public static class RegistrationRetentionDeadline
{
    public static DateTime? Resolve(int retentionPolicyId, DateTime createdAt, DateTime? anonymousUpperBoundUtc = null)
    {
        if (createdAt == default || createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Retention start must be UTC.", nameof(createdAt));
        }

        if (anonymousUpperBoundUtc is { Kind: not DateTimeKind.Utc })
            throw new ArgumentException("Anonymous retention bound must be UTC.", nameof(anonymousUpperBoundUtc));

        DateTime? deadline = (RegistrationRetentionPolicyEnum)retentionPolicyId switch
        {
            RegistrationRetentionPolicyEnum.StandardOperational => createdAt.AddDays(730),
            RegistrationRetentionPolicyEnum.SensitiveShort => createdAt.AddDays(90),
            RegistrationRetentionPolicyEnum.MarketingConsentEvidence => createdAt.AddDays(2555),
            RegistrationRetentionPolicyEnum.LegalHold => null,
            _ => throw new ArgumentOutOfRangeException(nameof(retentionPolicyId))
        };
        return deadline is { } finite && anonymousUpperBoundUtc is { } cap && cap < finite ? cap : deadline;
    }

    public static DateTime? ResolveUpdate(int retentionPolicyId, DateTime updatedAt,
        DateTime? existingRetentionUntil, DateTime? anonymousUpperBoundUtc)
    {
        DateTime? resolved = Resolve(retentionPolicyId, updatedAt, anonymousUpperBoundUtc);
        if (!anonymousUpperBoundUtc.HasValue) return resolved;
        if (!existingRetentionUntil.HasValue || !resolved.HasValue) return null;
        return existingRetentionUntil.Value < resolved.Value ? existingRetentionUntil : resolved;
    }
}
