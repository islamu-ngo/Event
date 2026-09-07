namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Category;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for CategoryDto (detail view).
/// </summary>
public sealed class CategoryDetailLinkPolicy : ILinkPolicy<CategoryDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(CategoryDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetCategoryById,
            new { id = dto.Id },
            "GET",
            dto.FullName);

        // Collection link
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetCategories,
            null,
            "GET",
            "All categories");

        // Parent category link (if has parent)
        if (dto.ParentId.HasValue)
        {
            yield return new LinkDefinition(
                "parent",
                RouteNames.GetCategoryById,
                new { id = dto.ParentId },
                "GET",
                dto.ParentFullName ?? "Parent category");
        }

        // Edit link - requires authentication
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateCategory,
            new { id = dto.Id },
            "PATCH",
            "Update category",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Category, dto);

        // Delete link - requires authentication
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteCategory,
            new { id = dto.Id },
            "DELETE",
            "Delete category",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.Category, dto);
    }
}

/// <summary>
/// Link policy for CategoryListDto (collection items).
/// </summary>
public sealed class CategoryCollectionLinkPolicy : ICollectionLinkPolicy<CategoryListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(CategoryListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetCategoryById,
            new { id = dto.Id },
            "GET",
            dto.FullName);

        // Parent link (if has parent)
        if (dto.ParentId.HasValue)
        {
            yield return new LinkDefinition(
                "parent",
                RouteNames.GetCategoryById,
                new { id = dto.ParentId },
                "GET",
                dto.ParentFullName ?? "Parent");
        }
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires authentication
        yield return new LinkDefinition(
            "create",
            RouteNames.CreateCategory,
            null,
            "POST",
            "Create new category",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(CategoryDto), "category");
    }
}
