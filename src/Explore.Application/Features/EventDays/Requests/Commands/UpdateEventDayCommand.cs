using Explore.Application.Authorization;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventDays.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventDay, AuthorizationActions.Update)]
public sealed record UpdateEventDayCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventDayId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required UpdateEventDayDto EventDayDto { get; init; }

    string? ISecureRequest.ResourceId => EventDayId.ToString();
}
