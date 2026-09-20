using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessions.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Create)]
public sealed record CreateDraftEventSessionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateDraftEventSessionRequestDto Request { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => Request.EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new PreCreateAuthorizationFacts(TenantId, Request.EventId, null, null);
}
