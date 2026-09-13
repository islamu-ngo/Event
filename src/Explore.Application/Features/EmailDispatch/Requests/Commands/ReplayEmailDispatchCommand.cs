using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EmailDispatch.Requests.Commands;

[AuthorizeResource(ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.Replay)]
public sealed record ReplayEmailDispatchCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid OutboxId { get; init; }
    public Guid? ChangedBy { get; init; }

    string? ISecureRequest.ResourceId => OutboxId == Guid.Empty ? null : OutboxId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
