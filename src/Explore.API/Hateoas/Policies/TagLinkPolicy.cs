namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Tag;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for TagDto (detail view).
/// </summary>
public sealed class TagDetailLinkPolicy : ILinkPolicy<TagDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(TagDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTagById,
            new { id = dto.Id },
            "GET",
            dto.FullName);

        // Collection link
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetTags,
            null,
            "GET",
            "All tags");

        // Edit link - requires authentication
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateTag,
            new { id = dto.Id },
            "PATCH",
            "Update tag",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Tag, dto);

        // Delete link - requires authentication
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteTag,
            new { id = dto.Id },
            "DELETE",
            "Delete tag",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.Tag, dto);
    }
}

/// <summary>
/// Link policy for TagListDto (collection items).
/// </summary>
public sealed class TagCollectionLinkPolicy : ICollectionLinkPolicy<TagListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(TagListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTagById,
            new { id = dto.Id },
            "GET",
            dto.FullName);
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires authentication
        yield return new LinkDefinition(
            "create",
            RouteNames.CreateTag,
            null,
            "POST",
            "Create new tag",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(TagDto), "tag");
    }
}
