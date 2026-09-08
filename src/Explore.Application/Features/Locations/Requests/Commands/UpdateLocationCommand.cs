using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Location;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Locations.Requests.Commands;

[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Update)]
public sealed record UpdateLocationCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid LocationId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateLocationDto UpdateLocationDto { get; init; }

    string? ISecureRequest.ResourceId => LocationId.ToString();
}
