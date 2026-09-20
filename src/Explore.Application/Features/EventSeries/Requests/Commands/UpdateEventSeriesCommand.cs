using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSeries.Requests.Commands;

[AuthorizeResource(ResourceKinds.Actor, AuthorizationActions.Update)]
public sealed record UpdateEventSeriesCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid EventSeriesId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required UpdateEventSeriesDto EventSeriesDto { get; init; }
}
