using Explore.Domain.Enums;

namespace Explore.Application.Features.ExternalApiKeys;

/// <summary>
/// Default credit/quota configuration per owner type, applied when no explicit quota is specified.
/// </summary>
public static class ExternalApiKeyQuotaDefaults
{
    /// <summary>
    /// Returns the default credit period for the given owner type.
    /// </summary>
    public static ExternalApiKeyCreditPeriodEnum GetDefaultCreditPeriod(ExternalApiKeyOwnerType ownerType)
    {
        return ownerType switch
        {
            ExternalApiKeyOwnerType.User => ExternalApiKeyCreditPeriodEnum.Daily,
            ExternalApiKeyOwnerType.Organization => ExternalApiKeyCreditPeriodEnum.Monthly,
            ExternalApiKeyOwnerType.Group => ExternalApiKeyCreditPeriodEnum.Monthly,
            ExternalApiKeyOwnerType.Tenant => ExternalApiKeyCreditPeriodEnum.Monthly,
            ExternalApiKeyOwnerType.InstanceAdmin => ExternalApiKeyCreditPeriodEnum.None,
            _ => ExternalApiKeyCreditPeriodEnum.Daily
        };
    }

    /// <summary>
    /// Returns the default credit limit per period for the given owner type.
    /// Null means unlimited (only for InstanceAdmin).
    /// </summary>
    public static int? GetDefaultCreditLimit(ExternalApiKeyOwnerType ownerType)
    {
        return ownerType switch
        {
            ExternalApiKeyOwnerType.User => 1_000,
            ExternalApiKeyOwnerType.Organization => 10_000,
            ExternalApiKeyOwnerType.Group => 5_000,
            ExternalApiKeyOwnerType.Tenant => 50_000,
            ExternalApiKeyOwnerType.InstanceAdmin => null,
            _ => 1_000
        };
    }

    /// <summary>
    /// Returns the default max rollover credits for the given owner type.
    /// Zero means no rollover. Null means unlimited.
    /// </summary>
    public static int? GetDefaultMaxRolloverCredits(ExternalApiKeyOwnerType ownerType)
    {
        return ownerType switch
        {
            ExternalApiKeyOwnerType.User => 0,
            ExternalApiKeyOwnerType.Organization => 5_000,
            ExternalApiKeyOwnerType.Group => 2_500,
            ExternalApiKeyOwnerType.Tenant => 25_000,
            ExternalApiKeyOwnerType.InstanceAdmin => null,
            _ => 0
        };
    }
}
