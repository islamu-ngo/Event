namespace Explore.API.Hateoas.Assemblers;

using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Resource assembler for OrganizationMember entities (relationship with payload).
/// Converts OrganizationMemberDto to HAL resources with appropriate links.
/// Note: OrganizationMember uses same DTO for detail and list views.
/// </summary>
public sealed class OrganizationMemberResourceAssembler : ResourceAssemblerBase<OrganizationMemberDto, OrganizationMemberDto>
{
    public OrganizationMemberResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<OrganizationMemberDto> detailLinkPolicy,
        ICollectionLinkPolicy<OrganizationMemberDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    public override async Task<HalCollectionResource<OrganizationMemberDto>> ToCollectionResource(
        IEnumerable<OrganizationMemberDto> items,
        string routeName,
        object? additionalRouteValues,
        HttpContext httpContext)
    {
        var resource = await base.ToCollectionResource(items, routeName, additionalRouteValues, httpContext);
        if (TryGetOrganizationId(additionalRouteValues, out var organizationId))
        {
            var hasTenant = TryGetTenantId(additionalRouteValues, out var tenantId);
            var organizationScopedFacts = new OrganizationMemberAuthorizationFacts(
                hasTenant ? tenantId : Guid.Empty,
                organizationId,
                MemberId: null,
                UserId: null);
            var authorizationScope = hasTenant
                ? new AuthorizationScope(
                    TenantId: tenantId.ToString(),
                    OrganizationId: organizationId.ToString())
                : new AuthorizationScope(OrganizationId: organizationId.ToString());

            var createLinks = await GenerateLinks([
                new LinkDefinition(
                    LinkRelations.Create,
                    RouteNames.AddOrganizationMember,
                    null,
                    "POST",
                    "Add organization member",
                    RequiresAuth: true)
                    .RequirePermission(
                        AuthorizationActions.Create,
                        ResourceKinds.OrganizationMember,
                        organizationId.ToString(),
                        authorizationScope,
                        organizationScopedFacts)
            ], httpContext.User, httpContext);

            foreach (var link in createLinks)
            {
                resource.Links[link.Key] = link.Value;
            }
        }

        return resource;
    }

    /// <summary>
    /// Override to provide embedded resources for organization member details.
    /// </summary>
    protected override Dictionary<string, object>? GetEmbeddedResources(
        OrganizationMemberDto dto,
        HttpContext httpContext)
    {
        // Members link to User, Organization, Role via _links
        return null;
    }

    private static bool TryGetOrganizationId(object? routeValues, out Guid organizationId)
        => TryGetGuidRouteValue(routeValues, "organizationId", out organizationId);

    private static bool TryGetTenantId(object? routeValues, out Guid tenantId)
        => TryGetGuidRouteValue(routeValues, "tenantId", out tenantId);

    private static bool TryGetGuidRouteValue(object? routeValues, string name, out Guid value)
    {
        value = default;
        if (routeValues is null)
        {
            return false;
        }

        if (routeValues is IReadOnlyDictionary<string, object> readOnlyDictionary)
        {
            readOnlyDictionary.TryGetValue(name, out var routeValue);
            value = default;
            return TrySetGuid(routeValue, out value);
        }

        if (routeValues is IDictionary<string, object> dictionary)
        {
            dictionary.TryGetValue(name, out var routeValue);
            value = default;
            return TrySetGuid(routeValue, out value);
        }

        return TrySetGuid(routeValues.GetType().GetProperty(name)?.GetValue(routeValues), out value);
    }

    private static bool TrySetGuid(object? value, out Guid guid)
    {
        guid = default;
        return value switch
        {
            Guid typedValue => SetGuid(typedValue, out guid),
            string text => Guid.TryParse(text, out guid),
            _ => false
        };
    }

    private static bool SetGuid(Guid value, out Guid guid)
    {
        guid = value;
        return value != Guid.Empty;
    }
}
