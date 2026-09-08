using System;
using System.Collections.Generic;
using Explore.Application.Authorization;
using Explore.Application.DTOs.OrganizationMember;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Requests.Queries;

[AuthorizeResource(ResourceKinds.OrganizationMember, AuthorizationActions.OrganizationMembers.View)]
public sealed record GetOrganizationMembersRequest : IRequest<List<OrganizationMemberDto>>, ISecureRequest
{
    public Guid OrganizationId { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => OrganizationId == Guid.Empty ? null : OrganizationId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new OrganizationMemberAuthorizationFacts(TenantId, OrganizationId, null, null);
}
