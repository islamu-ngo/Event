using System;
using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Requests.Commands;

[AuthorizeResource(ResourceKinds.OrganizationMember, AuthorizationActions.Delete)]
public sealed record DeleteOrganizationMemberCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid MemberId { get; init; }
    public required string RequesterUserId { get; init; }

    string? ISecureRequest.ResourceId => MemberId.ToString();
}
