using Explore.Application.Authorization;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventDays.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventDay, AuthorizationActions.Create)]
public sealed record CreateEventDayCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventDayDto EventDayDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
