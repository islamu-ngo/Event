using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class CustomPropertyProjectionAdminHateoasTests
{
    [Test]
    public async Task ProjectionStatusLinks_ExposePermissionMetadataForAdminActions()
    {
        var tenantId = Guid.NewGuid();
        var dto = new ProjectionStatusDto
        {
            TenantId = tenantId,
            ProjectionName = IEventCustomPropertyProjectionUpdater.ProjectionName,
            ProjectionVersion = 3
        };
        var policy = new ProjectionStatusDetailLinkPolicy();

        var links = policy.GetLinks(dto, user: null).ToList();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetCustomPropertyProjectionStatus);
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyProjection);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyProjections.View);
        await Assert.That(GetRouteValue<Guid>(self.RouteValues, "tenantId")).IsEqualTo(tenantId);
        await Assert.That(self.PermissionFacts)
            .IsEqualTo(new CustomPropertyProjectionAuthorizationFacts(tenantId));

        var rebuild = links.Single(link => link.Rel == "rebuild");
        await Assert.That(rebuild.RouteName).IsEqualTo(RouteNames.RebuildCustomPropertyProjection);
        await Assert.That(rebuild.RequiresAuth).IsTrue();
        await Assert.That(rebuild.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyProjection);
        await Assert.That(rebuild.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyProjections.Update);
        await Assert.That(rebuild.PermissionFacts)
            .IsEqualTo(new CustomPropertyProjectionAuthorizationFacts(tenantId));

        var drainDirtyScopes = links.Single(link => link.Rel == "drain-dirty-scopes");
        await Assert.That(drainDirtyScopes.RouteName).IsEqualTo(RouteNames.DrainCustomPropertyProjectionDirtyScopes);
        await Assert.That(drainDirtyScopes.RequiresAuth).IsTrue();
        await Assert.That(drainDirtyScopes.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyProjection);
        await Assert.That(drainDirtyScopes.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyProjections.Update);

        var dirtyScopes = links.Single(link => link.Rel == "dirty-scopes");
        await Assert.That(dirtyScopes.RouteName).IsEqualTo(RouteNames.GetCustomPropertyProjectionDirtyScopes);
        await Assert.That(dirtyScopes.RequiresAuth).IsTrue();
        await Assert.That(dirtyScopes.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyProjection);
        await Assert.That(dirtyScopes.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyProjections.View);
        await Assert.That(GetRouteValue<Guid>(dirtyScopes.RouteValues, "tenantId")).IsEqualTo(tenantId);
        await Assert.That(GetRouteValue<string>(dirtyScopes.RouteValues, "projectionName")).IsEqualTo(dto.ProjectionName);
    }


    [Test]
    public async Task SessionProjectionStatusLinks_UseSessionStatusAndRebuildRoutes()
    {
        var tenantId = Guid.NewGuid();
        var dto = new ProjectionStatusDto
        {
            TenantId = tenantId,
            ProjectionName = IEventSessionCustomPropertyProjectionUpdater.ProjectionName,
            ProjectionVersion = 1
        };
        var policy = new ProjectionStatusDetailLinkPolicy();

        var links = policy.GetLinks(dto, user: null).ToList();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        var rebuild = links.Single(link => link.Rel == "rebuild");

        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetSessionCustomPropertyProjectionStatus);
        await Assert.That(rebuild.RouteName).IsEqualTo(RouteNames.RebuildSessionCustomPropertyProjection);
        await Assert.That(GetRouteValue<Guid>(self.RouteValues, "tenantId")).IsEqualTo(tenantId);
        await Assert.That(rebuild.PermissionFacts)
            .IsEqualTo(new CustomPropertyProjectionAuthorizationFacts(dto.TenantId));
    }

    [Test]
    public async Task DirtyScopeLinks_ExposePermissionMetadataForDrainActions()
    {
        var tenantId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var dto = new ProjectionDirtyScopeDto
        {
            Id = 42,
            TenantId = tenantId,
            ProjectionName = IEventCustomPropertyProjectionUpdater.ProjectionName,
            ScopeType = CustomPropertyProjectionScopeType.Event,
            ScopeId = scopeId
        };
        var policy = new ProjectionDirtyScopeCollectionLinkPolicy();

        var itemLinks = policy.GetItemLinks(dto, user: null).ToList();
        var collectionLinks = policy.GetCollectionLinks(user: null).ToList();

        var drain = itemLinks.Single(link => link.Rel == "drain");
        await Assert.That(drain.RouteName).IsEqualTo(RouteNames.DrainCustomPropertyProjectionDirtyScopes);
        await Assert.That(drain.RequiresAuth).IsTrue();
        await Assert.That(drain.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyProjection);
        await Assert.That(drain.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyProjections.Update);
        await Assert.That(drain.PermissionResourceId).IsEqualTo("42");
        // The projection name and dirty scope name the row; tenant administration decides who may act on it.
        await Assert.That(drain.PermissionFacts)
            .IsEqualTo(new CustomPropertyProjectionAuthorizationFacts(tenantId));

        var drainAll = collectionLinks.Single(link => link.Rel == "drain-all");
        await Assert.That(drainAll.RouteName).IsEqualTo(RouteNames.DrainCustomPropertyProjectionDirtyScopes);
        await Assert.That(drainAll.RequiresAuth).IsTrue();
        await Assert.That(drainAll.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyProjection);
        await Assert.That(drainAll.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyProjections.Update);
    }

    [Test]
    public async Task ProjectionMutationResponseStatusLinks_AreAuthOnlyNavigationLinks()
    {
        var rebuildPolicy = new RebuildProjectionResponseLinkPolicy();
        var drainPolicy = new DrainDirtyScopesResponseLinkPolicy();

        var rebuildStatus = rebuildPolicy.GetLinks(new RebuildProjectionResponseDto(), user: null).Single();
        var drainStatus = drainPolicy.GetLinks(new DrainDirtyScopesResponseDto(), user: null).Single();

        await Assert.That(rebuildStatus.Rel).IsEqualTo("status");
        await Assert.That(rebuildStatus.RouteName).IsEqualTo(RouteNames.GetCustomPropertyProjectionStatus);
        await Assert.That(rebuildStatus.RequiresAuth).IsTrue();
        await Assert.That(rebuildStatus.PermissionAction).IsNull();

        await Assert.That(drainStatus.Rel).IsEqualTo("status");
        await Assert.That(drainStatus.RouteName).IsEqualTo(RouteNames.GetCustomPropertyProjectionStatus);
        await Assert.That(drainStatus.RequiresAuth).IsTrue();
        await Assert.That(drainStatus.PermissionAction).IsNull();
    }

    private static T? GetRouteValue<T>(object? routeValues, string name)
    {
        if (routeValues is null)
            return default;

        var property = routeValues.GetType().GetProperty(name);
        var value = property?.GetValue(routeValues);
        return value is T typedValue ? typedValue : default;
    }

}
