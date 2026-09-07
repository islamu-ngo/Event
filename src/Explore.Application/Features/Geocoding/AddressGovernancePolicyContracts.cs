using Explore.Domain.Enums;

namespace Explore.Application.Features.Geocoding;

public interface IAddressGovernancePolicyResolver
{
    Task<AddressGovernancePolicyDecision> ResolveAsync(
        AddressGovernancePolicyRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AddressGovernancePolicyRequest(
    Guid? TenantId,
    Guid? ActorId,
    Guid? UserId,
    Guid? OrganizationId = null);

public sealed record AddressGovernancePolicyDecision(
    bool CanCreateManualAddress,
    AddressCreationMode EffectiveMode,
    LocationAddressSourceEnum InitialSource,
    LocationAddressVisibilityEnum InitialVisibility,
    Guid? AddressOrganizationId)
{
    public static AddressGovernancePolicyDecision Denied(AddressCreationMode mode) => new(
        false,
        mode,
        LocationAddressSourceEnum.UnknownLegacy,
        LocationAddressVisibilityEnum.Quarantined,
        null);

    public static AddressGovernancePolicyDecision Allowed(
        AddressCreationMode mode,
        LocationAddressVisibilityEnum visibility,
        Guid? organizationId = null) => new(
        true,
        mode,
        LocationAddressSourceEnum.Manual,
        visibility,
        organizationId);

    internal bool IsValidManualDecision(Guid? trustedOrganizationId)
    {
        if (!CanCreateManualAddress || InitialSource != LocationAddressSourceEnum.Manual)
        {
            return false;
        }

        return InitialVisibility switch
        {
            LocationAddressVisibilityEnum.CreatorPrivate or LocationAddressVisibilityEnum.TenantApproved =>
                AddressOrganizationId is null,
            LocationAddressVisibilityEnum.OrganizationScoped =>
                trustedOrganizationId is { } organizationId
                && organizationId != Guid.Empty
                && AddressOrganizationId == organizationId,
            _ => false
        };
    }
}
