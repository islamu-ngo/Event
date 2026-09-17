using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Create)]
public sealed record ImportEventCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required ImportEventRequestDto Request { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => TenantId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
