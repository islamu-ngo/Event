namespace Explore.Domain.Enums;

public enum AiProviderTrustTierEnum
{
    LocalInProcessOrSameNetworkModel = 0,
    TenantControlledPrivateEndpoint = 1,
    TenantConfiguredExternalProcessor = 2,
    PlatformConfiguredExternalProcessor = 3,
    Unknown = 4
}
