namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for OrganizationReviewDto (detail view).
/// Provides links for organization review operations.
/// </summary>
public sealed class OrganizationReviewDetailLinkPolicy : ILinkPolicy<OrganizationReviewDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(OrganizationReviewDto dto, ClaimsPrincipal? user)
    {
        // Collection link
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetOrganizationReviews,
            null,
            "GET",
            "All reviews");

        // Organization link
        yield return new LinkDefinition(
            "organization",
            RouteNames.GetOrganizationById,
            new { id = dto.OrganizationId },
            "GET",
            dto.OrganizationFullName);

    }
}

/// <summary>
/// Link policy for OrganizationReviewDto in collection context.
/// </summary>
public sealed class OrganizationReviewCollectionLinkPolicy : ICollectionLinkPolicy<OrganizationReviewDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(OrganizationReviewDto dto, ClaimsPrincipal? user)
    {
        // Organization link
        yield return new LinkDefinition(
            "organization",
            RouteNames.GetOrganizationById,
            new { id = dto.OrganizationId },
            "GET",
            dto.OrganizationFullName);
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetOrganizationReviews,
            null,
            "GET",
            "All reviews");

        // Create link - requires authentication
        yield return new LinkDefinition(
            "create",
            RouteNames.CreateOrganizationReview,
            null,
            "POST",
            "Create review",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(OrganizationReviewDto), "organization_review");
    }
}
