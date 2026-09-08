namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for LocationRoomDto (detail view).
/// </summary>
public sealed class LocationRoomDetailLinkPolicy : ILinkPolicy<LocationRoomDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(LocationRoomDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetLocationRoomById,
            new { id = dto.Id },
            "GET",
            dto.Name);

        // Parent location link
        yield return new LinkDefinition(
            "location",
            RouteNames.GetLocationById,
            new { id = dto.LocationId },
            "GET",
            dto.LocationFullName);

        // Sibling rooms collection
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetLocationRoomsByLocation,
            new { locationId = dto.LocationId },
            "GET",
            "Location rooms");

        // Edit link - requires authentication and permission
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateLocationRoom,
            new { id = dto.Id },
            "PATCH",
            "Update room",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.LocationRoom, dto);

        // Delete link - requires authentication and permission
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteLocationRoom,
            new { id = dto.Id },
            "DELETE",
            "Delete room",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.LocationRoom, dto);
    }
}

/// <summary>
/// Link policy for LocationRoomListDto (collection items).
/// </summary>
public sealed class LocationRoomCollectionLinkPolicy : ICollectionLinkPolicy<LocationRoomListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(LocationRoomListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetLocationRoomById,
            new { id = dto.Id },
            "GET",
            dto.Name);

        // Parent location link
        yield return new LinkDefinition(
            "location",
            RouteNames.GetLocationById,
            new { id = dto.LocationId },
            "GET",
            "Parent location");
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires authentication
        yield return new LinkDefinition(
            "create",
            RouteNames.CreateLocationRoom,
            null,
            "POST",
            "Create new room",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(LocationRoomDto), "location_room");
    }
}
