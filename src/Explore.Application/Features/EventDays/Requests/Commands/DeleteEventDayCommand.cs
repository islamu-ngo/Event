using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventDays.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventDay, AuthorizationActions.Delete)]
public sealed record DeleteEventDayCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
