using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetPublicEventDetailsRequest : IQuery<EventDto?>
{
    public required string SlugCode { get; init; }
}
