using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Geocoding.Requests.Commands;

[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Locations.ApproveTenantAddress)]
public sealed record PromoteLocationAddressCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid LocationId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
}
