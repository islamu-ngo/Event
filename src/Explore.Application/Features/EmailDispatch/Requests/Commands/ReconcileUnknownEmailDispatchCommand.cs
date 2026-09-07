using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Requests.Commands;

[AuthorizeResource(ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.Reconcile)]
public sealed record ReconcileUnknownEmailDispatchCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid OutboxId { get; init; }
    public EmailDispatchUnknownReconciliationOutcome Outcome { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? ProviderMessageId { get; init; }
    public Guid? ChangedBy { get; init; }

    string? ISecureRequest.ResourceId => OutboxId == Guid.Empty ? null : OutboxId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
