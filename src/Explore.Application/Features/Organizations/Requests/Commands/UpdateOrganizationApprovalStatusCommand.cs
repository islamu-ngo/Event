using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Organization;
using MediatR;

namespace Explore.Application.Features.Organizations.Requests.Commands;

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Update)]
public sealed record UpdateOrganizationApprovalStatusCommand : IRequest<Unit>, ISecureRequest
{
    public Guid OrganizationId { get; init; }

    public required UpdateOrganizationApprovalStatusDto ApprovalStatusDto { get; init; }

    string? ISecureRequest.ResourceId => OrganizationId.ToString();
}
