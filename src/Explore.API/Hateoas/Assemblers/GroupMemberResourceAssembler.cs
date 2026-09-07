namespace Explore.API.Hateoas.Assemblers;

using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

public sealed class GroupMemberResourceAssembler : ResourceAssemblerBase<GroupMemberDto, GroupMemberDto>
{
    public GroupMemberResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<GroupMemberDto> detailLinkPolicy,
        ICollectionLinkPolicy<GroupMemberDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    public override async Task<HalCollectionResource<GroupMemberDto>> ToCollectionResource(
        IEnumerable<GroupMemberDto> items,
        string routeName,
        object? additionalRouteValues,
        HttpContext httpContext)
    {
        var resource = await base.ToCollectionResource(items, routeName, additionalRouteValues, httpContext);
        if (TryGetGroupId(additionalRouteValues, out var groupId))
        {
            var groupScopedFacts = new GroupMemberAuthorizationFacts(
                Guid.Empty,
                groupId,
                OrganizationId: null,
                UserId: null);

            var createLinks = await GenerateLinks([
                new LinkDefinition(
                    LinkRelations.Create,
                    RouteNames.CreateGroupMember,
                    null,
                    "POST",
                    "Add group member",
                    RequiresAuth: true)
                    .RequirePermission(
                        AuthorizationActions.Create,
                        ResourceKinds.GroupMember,
                        groupId.ToString(),
                        facts: groupScopedFacts)
            ], httpContext.User, httpContext);

            foreach (var link in createLinks)
            {
                resource.Links[link.Key] = link.Value;
            }
        }

        return resource;
    }

    protected override Dictionary<string, object>? GetEmbeddedResources(
        GroupMemberDto dto,
        HttpContext httpContext)
    {
        return null;
    }

    private static bool TryGetGroupId(object? routeValues, out Guid groupId)
    {
        groupId = default;
        if (routeValues is null)
        {
            return false;
        }

        object? value = null;
        if (routeValues is IReadOnlyDictionary<string, object> readOnlyDictionary)
        {
            readOnlyDictionary.TryGetValue("groupId", out value);
        }
        else if (routeValues is IDictionary<string, object> dictionary)
        {
            dictionary.TryGetValue("groupId", out value);
        }
        else
        {
            value = routeValues.GetType().GetProperty("groupId")?.GetValue(routeValues);
        }

        return value switch
        {
            Guid typedGroupId => SetGroupId(typedGroupId, out groupId),
            string text => Guid.TryParse(text, out groupId),
            _ => false
        };
    }

    private static bool SetGroupId(Guid value, out Guid groupId)
    {
        groupId = value;
        return value != Guid.Empty;
    }
}
