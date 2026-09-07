using Explore.Application.Authorization;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Requests.Queries;

[AuthorizeResource(ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.View)]
public sealed record GetEmailDispatchStatusQuery : IRequest<BaseCommandResponse<IReadOnlyList<EmailDispatchStatusDto>>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public int Limit { get; init; } = 50;

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
