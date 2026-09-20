using Explore.Application.Authorization;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventDays.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventDay, AuthorizationActions.Create)]
public sealed record CreateEventDayCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventDayDto EventDayDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
