using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Requests.Commands;

[AuthorizeResource(ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.ManageTenant)]
public sealed record SetEmailDispatchTenantPauseStateCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public bool IsPaused { get; init; }
    public string? PauseReason { get; init; }
    public Guid? ChangedBy { get; init; }

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
