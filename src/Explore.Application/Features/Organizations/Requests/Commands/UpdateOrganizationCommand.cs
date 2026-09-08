using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Organization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Organizations.Requests.Commands;

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Update)]
public sealed record UpdateOrganizationCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid OrganizationId { get; init; }

    public required string UserId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateOrganizationDto UpdateOrganizationDto { get; init; }

    string? ISecureRequest.ResourceId => OrganizationId.ToString();
}
