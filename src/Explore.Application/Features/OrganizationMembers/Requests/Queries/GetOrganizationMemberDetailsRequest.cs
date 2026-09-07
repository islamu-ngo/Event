using Explore.Application.Authorization;
using Explore.Application.DTOs.OrganizationMember;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Requests.Queries;

[AuthorizeResource(ResourceKinds.OrganizationMember, AuthorizationActions.OrganizationMembers.View)]
public sealed record GetOrganizationMemberDetailsRequest : IRequest<OrganizationMemberDto?>, ISecureRequest
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => Id == Guid.Empty ? null : Id.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new OrganizationMemberAuthorizationFacts(TenantId, Guid.Empty, null, null);
}
