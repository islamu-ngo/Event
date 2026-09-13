using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Location;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Locations.Requests.Commands;

[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Update)]
public sealed record UpdateLocationCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid LocationId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateLocationDto UpdateLocationDto { get; init; }

    string? ISecureRequest.ResourceId => LocationId.ToString();
}
