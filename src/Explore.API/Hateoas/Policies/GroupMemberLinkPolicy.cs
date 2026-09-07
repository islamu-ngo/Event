namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Hateoas;

public sealed class GroupMemberDetailLinkPolicy : ILinkPolicy<GroupMemberDto>
{
    public IEnumerable<LinkDefinition> GetLinks(GroupMemberDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetGroupMemberById,
            new { id = dto.Id },
            "GET",
            $"{dto.UserFullName} - {dto.RoleName}");

        yield return new LinkDefinition(
            "group",
            RouteNames.GetGroupById,
            new { id = dto.GroupId },
            "GET",
            dto.GroupFullName);

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateGroupMember,
            new { id = dto.Id },
            "PUT",
            "Update membership",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.GroupMember, dto);

        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteGroupMember,
            new { id = dto.Id },
            "DELETE",
            "Remove member",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.GroupMember, dto);
    }
}

public sealed class GroupMemberCollectionLinkPolicy : ICollectionLinkPolicy<GroupMemberDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(GroupMemberDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetGroupMemberById,
            new { id = dto.Id },
            "GET",
            $"{dto.UserFullName} - {dto.RoleName}");

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateGroupMember,
            new { id = dto.Id },
            "PUT",
            "Update membership",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.GroupMember, dto);

        yield return new LinkDefinition(
            LinkRelations.Delete,
            RouteNames.DeleteGroupMember,
            new { id = dto.Id },
            "DELETE",
            "Remove member",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.GroupMember, dto);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
