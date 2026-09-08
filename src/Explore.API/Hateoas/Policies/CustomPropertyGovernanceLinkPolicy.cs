using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

/// <summary>
/// Link policy for CustomPropertyGovernanceRowDto collection items.
/// Each row links to the projection status for the tenant.
/// </summary>
public sealed class CustomPropertyGovernanceCollectionLinkPolicy : ICollectionLinkPolicy<CustomPropertyGovernanceRowDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(CustomPropertyGovernanceRowDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            "projection-status",
            RouteNames.GetCustomPropertyProjectionStatus,
            new { tenantId = dto.TenantId });
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(
            RouteNames.GetCustomPropertyGovernanceReport);
    }
}
