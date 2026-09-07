using Explore.Application.Authorization;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Requests.Commands;

[AuthorizeResource(ResourceKinds.OrganizationMember, AuthorizationActions.Create)]
public sealed record AddOrganizationMemberCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required AddOrganizationMemberDto AddOrganizationMemberDto { get; init; }
    public required string RequesterUserId { get; init; } // To check permissions
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => AddOrganizationMemberDto.OrganizationId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new OrganizationMemberAuthorizationFacts(TenantId, AddOrganizationMemberDto.OrganizationId, null, null);
}
