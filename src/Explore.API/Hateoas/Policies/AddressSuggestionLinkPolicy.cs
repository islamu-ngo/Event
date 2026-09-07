using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class AddressSuggestionDetailLinkPolicy
    : ILinkPolicy<AddressSuggestionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        AddressSuggestionDto dto,
        ClaimsPrincipal? user)
    {
        if (dto.LocationId is not { } locationId || locationId == Guid.Empty)
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetLocationById,
            new { id = locationId },
            "GET",
            dto.DisplayName);

        if (dto.Visibility != LocationAddressVisibilityEnum.TenantApproved)
        {
            yield return new LinkDefinition(
                LinkRelations.ApproveTenantAddress,
                RouteNames.ApproveTenantAddress,
                new { id = locationId },
                "POST",
                "Approve address for tenant reuse",
                RequiresAuth: true)
                .RequirePermission(
                    AuthorizationActions.Locations.ApproveTenantAddress,
                    ResourceDescriptors.AddressSuggestion,
                    dto);
        }
    }
}

public sealed class AddressSuggestionCollectionLinkPolicy
    : ICollectionLinkPolicy<AddressSuggestionDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        AddressSuggestionDto dto,
        ClaimsPrincipal? user)
    {
        if (dto.LocationId is not { } locationId || locationId == Guid.Empty)
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetLocationById,
            new { id = locationId },
            "GET",
            dto.DisplayName);

        if (dto.Visibility != LocationAddressVisibilityEnum.TenantApproved)
        {
            yield return new LinkDefinition(
                LinkRelations.ApproveTenantAddress,
                RouteNames.ApproveTenantAddress,
                new { id = locationId },
                "POST",
                "Approve address for tenant reuse",
                RequiresAuth: true)
                .RequirePermission(
                    AuthorizationActions.Locations.ApproveTenantAddress,
                    ResourceDescriptors.AddressSuggestion,
                    dto);
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.AddressSuggestions,
            RouteNames.GetAddressSuggestions,
            RouteValues: null,
            "POST",
            "Search address suggestions",
            RequiresAuth: true);
    }
}
