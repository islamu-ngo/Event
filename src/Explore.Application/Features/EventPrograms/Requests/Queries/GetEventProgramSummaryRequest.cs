using Explore.Application.Authorization;
using Explore.Application.DTOs.EventProgram;
using MediatR;

namespace Explore.Application.Features.EventPrograms.Requests.Queries;

public sealed record GetEventProgramSummaryRequest(Guid EventId) : IRequest<EventProgramSummaryDto?>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventProgramSummaryRequest : IRequest<EventProgramSummaryDto?>, ISecureRequest
{
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
