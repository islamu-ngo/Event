using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Requests.Commands;

[AuthorizeResource(ResourceKinds.OrganizationMember, AuthorizationActions.Update)]
public sealed record UpdateOrganizationMemberRoleCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required UpdateOrganizationMemberRoleDto UpdateOrganizationMemberRoleDto { get; init; }
    public required string RequesterUserId { get; init; }

    string? ISecureRequest.ResourceId => UpdateOrganizationMemberRoleDto.Id.ToString();
}
