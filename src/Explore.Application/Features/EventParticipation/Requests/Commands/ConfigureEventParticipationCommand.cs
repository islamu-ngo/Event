using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventParticipation.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrations)]
public sealed record ConfigureEventParticipationCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required ConfigureEventParticipationDto ParticipationConfiguration { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
