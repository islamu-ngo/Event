using System.Globalization;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

/// <summary>
/// Link policy for ProjectionStatusDto (detail view).
/// Provides operator-oriented links to rebuild and drain actions.
/// </summary>
public sealed class ProjectionStatusDetailLinkPolicy : ILinkPolicy<ProjectionStatusDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ProjectionStatusDto dto, ClaimsPrincipal? user)
    {
        var facts = ProjectionFacts(dto.TenantId);
        var resourceId = ProjectionResourceId(dto.TenantId, dto.ProjectionName);

        var isSessionProjection = string.Equals(
            dto.ProjectionName,
            IEventSessionCustomPropertyProjectionUpdater.ProjectionName,
            StringComparison.Ordinal);
        var statusRoute = isSessionProjection
            ? RouteNames.GetSessionCustomPropertyProjectionStatus
            : RouteNames.GetCustomPropertyProjectionStatus;
        var rebuildRoute = isSessionProjection
            ? RouteNames.RebuildSessionCustomPropertyProjection
            : RouteNames.RebuildCustomPropertyProjection;

        yield return LinkDefinition.Self(
            statusRoute,
            new { tenantId = dto.TenantId })
            .RequirePermission(AuthorizationActions.CustomPropertyProjections.View,
                ResourceKinds.CustomPropertyProjection,
                resourceId,
                facts: facts);

        yield return new LinkDefinition(
            "rebuild",
            rebuildRoute,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.CustomPropertyProjections.Update,
                ResourceKinds.CustomPropertyProjection,
                resourceId,
                facts: facts);

        yield return new LinkDefinition(
            "drain-dirty-scopes",
            RouteNames.DrainCustomPropertyProjectionDirtyScopes,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.CustomPropertyProjections.Update,
                ResourceKinds.CustomPropertyProjection,
                resourceId,
                facts: facts);

        yield return new LinkDefinition(
            "dirty-scopes",
            RouteNames.GetCustomPropertyProjectionDirtyScopes,
            new { tenantId = dto.TenantId, projectionName = dto.ProjectionName },
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.CustomPropertyProjections.View,
                ResourceKinds.CustomPropertyProjection,
                resourceId,
                facts: facts);
    }

    private static string ProjectionResourceId(Guid tenantId, string projectionName)
        => $"{tenantId:N}:{projectionName}";

    // Projection administration is tenant-scoped; the projection name selects the row.
    private static IAuthorizationFacts ProjectionFacts(Guid tenantId)
        => new CustomPropertyProjectionAuthorizationFacts(tenantId);
}


/// <summary>
/// Collection policy for ProjectionStatusDto list items.
/// Reuses the detail policy for list-item action affordances.
/// </summary>
public sealed class ProjectionStatusCollectionLinkPolicy : ICollectionLinkPolicy<ProjectionStatusDto>
{
    private readonly ProjectionStatusDetailLinkPolicy _detailPolicy = new();

    public IEnumerable<LinkDefinition> GetItemLinks(ProjectionStatusDto dto, ClaimsPrincipal? user)
        => _detailPolicy.GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

/// <summary>
/// Detail policy for ProjectionDirtyScopeDto resources.
/// Mirrors the collection item drain affordance when a dirty scope is rendered directly.
/// </summary>
public sealed class ProjectionDirtyScopeDetailLinkPolicy : ILinkPolicy<ProjectionDirtyScopeDto>
{
    private readonly ProjectionDirtyScopeCollectionLinkPolicy _collectionPolicy = new();

    public IEnumerable<LinkDefinition> GetLinks(ProjectionDirtyScopeDto dto, ClaimsPrincipal? user)
        => _collectionPolicy.GetItemLinks(dto, user);
}

/// <summary>
/// Link policy for RebuildProjectionResponseDto.
/// Provides navigation back to status after a rebuild.
/// </summary>
public sealed class RebuildProjectionResponseLinkPolicy : ILinkPolicy<RebuildProjectionResponseDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RebuildProjectionResponseDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            "status",
            RouteNames.GetCustomPropertyProjectionStatus,
            RequiresAuth: true);
    }
}

/// <summary>
/// Link policy for DrainDirtyScopesResponseDto.
/// Provides navigation back to status and dirty-scope inspection.
/// </summary>
public sealed class DrainDirtyScopesResponseLinkPolicy : ILinkPolicy<DrainDirtyScopesResponseDto>
{
    public IEnumerable<LinkDefinition> GetLinks(DrainDirtyScopesResponseDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            "status",
            RouteNames.GetCustomPropertyProjectionStatus,
            RequiresAuth: true);
    }
}

/// <summary>
/// Link policy for ProjectionDirtyScopeDto collection items.
/// </summary>
public sealed class ProjectionDirtyScopeCollectionLinkPolicy : ICollectionLinkPolicy<ProjectionDirtyScopeDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ProjectionDirtyScopeDto dto, ClaimsPrincipal? user)
    {
        // Scope type and id name the dirty row; tenant administration decides who may drain it.
        var facts = new CustomPropertyProjectionAuthorizationFacts(dto.TenantId);

        yield return new LinkDefinition(
            "drain",
            RouteNames.DrainCustomPropertyProjectionDirtyScopes,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.CustomPropertyProjections.Update,
                ResourceKinds.CustomPropertyProjection,
                dto.Id.ToString(CultureInfo.InvariantCulture),
                facts: facts);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            "drain-all",
            RouteNames.DrainCustomPropertyProjectionDirtyScopes,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.CustomPropertyProjections.Update,
                ResourceKinds.CustomPropertyProjection,
                "custom-property-projection-dirty-scopes");
    }
}
