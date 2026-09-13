using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventProgram;

namespace Explore.Application.Features.EventPrograms.Requests.Queries;

public sealed record GetEventProgramSummaryRequest(Guid EventId) : IQuery<EventProgramSummaryDto?>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventProgramSummaryRequest : IQuery<EventProgramSummaryDto?>, ISecureRequest
{
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
