using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

public enum LocationPrivacyGovernanceReasonCode
{
    Resolved = 1,
    InvalidTenantId = 2,
    InvalidInstanceSetting = 3,
    InvalidTenantSetting = 4,
    RepositoryUnavailable = 5
}

public enum LocationPrivacyGovernanceSource
{
    FailClosed = 0,
    ConservativeDefaults = 1,
    Instance = 2,
    InstanceAndTenant = 3
}

public sealed record LocationPrivacyGovernanceMetadata(
    LocationPrivacyGovernanceSource Source,
    string InstanceVersion,
    string? TenantVersion,
    string EffectiveVersion);

public sealed record EffectiveLocationPrivacyGovernance(
    bool IsResolved,
    LocationPrivacyGovernanceReasonCode ReasonCode,
    bool AllowHomeLocations,
    bool AllowPublicExactAddress,
    bool AllowPublicCoordinates,
    LocationDisclosureAudienceEnum MinimumHomeAudience,
    TimeSpan DefaultRevealOffset)
{
    public LocationPrivacyGovernanceMetadata Metadata { get; init; } = new(
        LocationPrivacyGovernanceSource.FailClosed,
        "fail-closed",
        null,
        "fail-closed");

    public static EffectiveLocationPrivacyGovernance FailClosed(
        LocationPrivacyGovernanceReasonCode reasonCode)
        => new(
            IsResolved: false,
            reasonCode,
            AllowHomeLocations: false,
            AllowPublicExactAddress: false,
            AllowPublicCoordinates: false,
            MinimumHomeAudience: LocationDisclosureAudienceEnum.Never,
            DefaultRevealOffset: TimeSpan.FromDays(30))
        {
            Metadata = new(
                LocationPrivacyGovernanceSource.FailClosed,
                "fail-closed",
                null,
                $"fail-closed:{reasonCode}")
        };
}

public interface ILocationPrivacyGovernanceService
{
    Task<EffectiveLocationPrivacyGovernance> ResolveAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
