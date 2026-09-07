using Explore.Domain.Enums;

namespace Explore.Domain;

public static class RegistrationRetentionDeadline
{
    public static DateTime? Resolve(int retentionPolicyId, DateTime createdAt)
    {
        if (createdAt == default || createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Retention start must be UTC.", nameof(createdAt));
        }

        return (RegistrationRetentionPolicyEnum)retentionPolicyId switch
        {
            RegistrationRetentionPolicyEnum.StandardOperational => createdAt.AddDays(730),
            RegistrationRetentionPolicyEnum.SensitiveShort => createdAt.AddDays(90),
            RegistrationRetentionPolicyEnum.MarketingConsentEvidence => createdAt.AddDays(2555),
            RegistrationRetentionPolicyEnum.LegalHold => null,
            _ => throw new ArgumentOutOfRangeException(nameof(retentionPolicyId))
        };
    }
}
