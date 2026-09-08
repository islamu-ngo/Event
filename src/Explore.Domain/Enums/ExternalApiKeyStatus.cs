namespace Explore.Domain.Enums;

public enum ExternalApiKeyStatusEnum
{
    Active = 1,
    Revoked = 2,
    Expired = 3,
    Suspended = 4,
    PendingRotation = 5
}
