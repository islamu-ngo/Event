namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.User;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for UserDto (detail view).
/// Provides links for user-related operations.
/// </summary>
public sealed class UserDetailLinkPolicy : ILinkPolicy<UserDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(UserDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetCurrentUser,
            null,
            HttpMethods.Get,
            "Current user",
            RequiresAuth: true);

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateCurrentUser,
            new { id = dto.Id },
            HttpMethods.Patch,
            "Update profile",
            RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.Update,
                ResourceKinds.User,
                dto.Id.ToString());

        // Actor link (user's actor profile)
        if (dto.ActorId != Guid.Empty)
        {
            yield return new LinkDefinition(
                "actor",
                RouteNames.GetActorById,
                new { id = dto.ActorId },
                "GET",
                dto.ActorDisplayName ?? "User profile");
        }

        // Organizations link
        yield return new LinkDefinition(
            "organizations",
            RouteNames.GetUserOrganizations,
            new { userId = dto.Id },
            "GET",
            "User's organizations",
            RequiresAuth: true);

    }
}

/// <summary>
/// Link policy for UserDto in collection context.
/// </summary>
public sealed class UserCollectionLinkPolicy : ICollectionLinkPolicy<UserDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(UserDto dto, ClaimsPrincipal? user)
    {
        // Actor link
        if (dto.ActorId != Guid.Empty)
        {
            yield return new LinkDefinition(
                "actor",
                RouteNames.GetActorById,
                new { id = dto.ActorId },
                "GET",
                dto.ActorDisplayName ?? "User profile");
        }
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Current user link - requires authentication
        yield return new LinkDefinition(
            "current-user",
            RouteNames.GetCurrentUser,
            null,
            "GET",
            "Get current user",
            RequiresAuth: true);
    }
}
