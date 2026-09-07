using System.Collections.Generic;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Organization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Organizations.Requests.Commands;

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Create)]
public sealed record CreateOrganizationCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public const string PreCreateResourceId = "create";
    public const string PreCreateAuthorizationPhase = AuthorizationPhases.PreCreate;

    public required CreateOrganizationDto OrganizationDto { get; init; }
    public required Guid CreatorUserId { get; init; }

    string? ISecureRequest.ResourceId => PreCreateResourceId;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new PreCreateAuthorizationFacts(Guid.Empty, null, null, null);
}
