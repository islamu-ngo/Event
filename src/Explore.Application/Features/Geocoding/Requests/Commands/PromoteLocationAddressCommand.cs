using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Geocoding.Requests.Commands;

[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Locations.ApproveTenantAddress)]
public sealed record PromoteLocationAddressCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid LocationId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
}
