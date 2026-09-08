using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAggregateView;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventAggregateViews.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.View)]
public sealed record GetEventWithSessionsAggregateViewQuery(
    Guid EventId,
    ExposureLevel ExposureCeiling) : IRequest<BaseCommandResponse<EventWithSessionsViewDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString();
}
